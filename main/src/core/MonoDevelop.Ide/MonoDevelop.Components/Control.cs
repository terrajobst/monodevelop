﻿//
// Control.cs
//
// Author:
//       Lluis Sanchez Gual <lluis@xamarin.com>
//
// Copyright (c) 2014 Xamarin, Inc (http://www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using MonoDevelop.Components.Commands;

#if MAC
using AppKit;
using MonoDevelop.Components.Mac;
#endif

namespace MonoDevelop.Components
{
	public class Control : IDisposable
	{
		internal static Dictionary<object, WeakReference<Control>> cache = new Dictionary<object, WeakReference<Control>> ();
		object nativeWidget;

		protected Control ()
		{
		}

		Control (object widget)
		{
			if (widget == null)
				throw new ArgumentNullException (nameof (widget));
			this.nativeWidget = widget;
			cache.Add (nativeWidget, new WeakReference<Control> (this));
		}

		protected virtual object CreateNativeWidget ()
		{
			throw new NotSupportedException ();
		}

		public T GetNativeWidget<T> ()
		{
			if (nativeWidget == null) {
				var toCache = this;
				var w = CreateNativeWidget ();
				if (!(w is T)) {
					var temp = w as Control;
					while (temp != null) {
						w = temp.GetNativeWidget<T> ();
						temp = w as Control;
					}
					w = ConvertToType (typeof (T), w);
				}
				if (w is Gtk.Widget) {
					var gtkWidget = (Gtk.Widget)w;
					var c = new CommandRouterContainer (gtkWidget, this, true);
					c.FocusChain = new [] { gtkWidget };
					c.Show ();
					nativeWidget = c;
					c.Destroyed += delegate {
						GC.SuppressFinalize (this);
						Dispose (true);
					};
					toCache = c;
				} else {
					nativeWidget = w;
				}
				WeakReference<Control> cached;
				Control target;
				if (cache.TryGetValue (nativeWidget, out cached) && cached.TryGetTarget (out target)) {
					if (target != toCache)
						throw new Exception ();
				} else
					cache.Add (nativeWidget, new WeakReference<Control> (toCache));
			}
			if (nativeWidget is T)
				return (T)nativeWidget;
			else
				throw new NotSupportedException ();
		}

		static object ConvertToType (Type t, object w)
		{
			if (t.IsInstanceOfType (w))
				return w;

#if MAC
			if (w is NSView && t == typeof (Gtk.Widget)) {
				var ww = GtkMacInterop.NSViewToGtkWidget ((NSView)w);
				ww.Show ();
				return ww;
			}
			if (w is Gtk.Widget && t == typeof (NSView)) {
				return new GtkEmbed ((Gtk.Widget)w);
			}
#endif
			throw new NotSupportedException ();
		}

		public static implicit operator Gtk.Widget (Control d)
		{
			return d.GetNativeWidget<Gtk.Widget> ();
		}

		public static implicit operator Control (Gtk.Widget d)
		{
			if (d == null)
				return null;

			return GetImplicit<Control, Gtk.Widget>(d) ?? new Control (d);
		}

		static T GetImplicit<T, U> (U native) where T : Control where U : class
		{
			WeakReference<Control> cached;
			Control target;

			if (cache.TryGetValue (native, out cached) && cached.TryGetTarget (out target))
				return (T)target;
			return null;
		}

		public void GrabFocus ()
		{
			// TODO
		}


		public bool HasFocus {
			get
			{
				// TODO
				if (nativeWidget is Gtk.Widget)
					return ((Gtk.Widget)nativeWidget).HasFocus;
				return false;
			}
		}

		public void Dispose ()
		{
			if (nativeWidget is Gtk.Widget) {
				((Gtk.Widget)nativeWidget).Destroy ();
				return;
			}
#if MAC
			else if (nativeWidget is NSView)
				((NSView)nativeWidget).Dispose ();
#endif

			Dispose (true);
		}

		protected virtual void Dispose (bool disposing)
		{
			cache.Remove (nativeWidget);
		}
	}
}

