using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using MonoDevelop.Core;
using Microsoft.VisualStudio.Text.Editor;

namespace MonoDevelop.Debugger.VSTextView.QuickInfo
{
	class DebuggerQuickInfoSource : IAsyncQuickInfoSource
	{
		readonly DebuggerQuickInfoSourceProvider provider;
		readonly ITextBuffer textBuffer;
		DebugValueWindow window;

		public DebuggerQuickInfoSource (DebuggerQuickInfoSourceProvider provider, ITextBuffer textBuffer)
		{
			this.provider = provider;
			this.textBuffer = textBuffer;
			DebuggingService.CurrentFrameChanged += CurrentFrameChanged;
			DebuggingService.StoppedEvent += TargetProcessExited;
		}

		void CurrentFrameChanged (object sender, EventArgs e)
		{
			if (window != null) {
				window.Destroy ();
				window = null;
			}
		}

		void TargetProcessExited (object sender, EventArgs e)
		{
			if (window == null)
				return;
			var debuggerSession = window.Tree.Frame?.DebuggerSession;
			if (debuggerSession == null || debuggerSession == sender) {
				window.Destroy ();
				window = null;
			}
		}

		public void Dispose ()
		{
			DebuggingService.CurrentFrameChanged -= CurrentFrameChanged;
			DebuggingService.StoppedEvent -= TargetProcessExited;
			if (window != null)
				Runtime.RunInMainThread (() => {
					window?.Destroy ();
					window = null;
				}).Ignore ();
		}

		public async Task<QuickInfoItem> GetQuickInfoItemAsync (IAsyncQuickInfoSession session, CancellationToken cancellationToken)
		{
			Task destroyTask = null;
			if (window != null)
				destroyTask = Runtime.RunInMainThread (() => {
					window?.Destroy ();
					window = null;
				});
			if (DebuggingService.CurrentFrame == null)
				return null;
			var view = session.TextView;

			var textViewLines = view.TextViewLines;
			var snapshot = textViewLines.FormattedSpan.Snapshot;
			var triggerPoint = session.GetTriggerPoint (textBuffer);
			var point = triggerPoint.GetPoint (snapshot);

			foreach (var debugInfoProvider in provider.debugInfoProviders) {
				var debugInfo = await debugInfoProvider.Value.GetDebugInfoAsync (point, cancellationToken);
				if (debugInfo.Text == null) {
					continue;
				}

				var options = DebuggingService.DebuggerSession.EvaluationOptions.Clone ();
				options.AllowMethodEvaluation = true;
				options.AllowTargetInvoke = true;

				var val = DebuggingService.CurrentFrame.GetExpressionValue (debugInfo.Text, options);

				if (val == null || val.IsUnknown || val.IsNotSupported)
					return null;

				if (!view.Properties.TryGetProperty (typeof (Gtk.Widget), out Gtk.Widget gtkParent))
					return null;
				provider.textDocumentFactoryService.TryGetTextDocument (textBuffer, out var textDocument);

				// This is a bit hacky, since AsyncQuickInfo is designed to display multiple elements if multiple sources
				// return value, we don't want that for debugger value hovering, hence we dismiss AsyncQuickInfo
				// and do our own thing, notice VS does same thing
				await session.DismissAsync ();
				if (destroyTask != null)
					await destroyTask;
				await provider.joinableTaskContext.Factory.SwitchToMainThreadAsync ();
				val.Name = debugInfo.Text;
				window = new DebugValueWindow ((Gtk.Window)gtkParent.Toplevel, textDocument?.FilePath, textBuffer.CurrentSnapshot.GetLineNumberFromPosition (debugInfo.Span.GetStartPoint (textBuffer.CurrentSnapshot)), DebuggingService.CurrentFrame, val, null);
				Ide.IdeApp.CommandService.RegisterTopWindow (window);
				var bounds = view.TextViewLines.GetCharacterBounds (point);
#if MAC
				var cocoaView = ((ICocoaTextView)view);
				var cgPoint = cocoaView.VisualElement.ConvertPointToView (new CoreGraphics.CGPoint (bounds.Left - view.ViewportLeft, bounds.Top - view.ViewportTop), cocoaView.VisualElement.Superview);
				cgPoint.Y = cocoaView.VisualElement.Superview.Frame.Height - cgPoint.Y;
				window.ShowPopup (gtkParent, new Gdk.Rectangle ((int)cgPoint.X, (int)cgPoint.Y, (int)bounds.Width, (int)bounds.Height), Components.PopupPosition.TopLeft);
#else
				throw new NotImplementedException ();
#endif
				return null;
			}
			return null;
		}
	}
}