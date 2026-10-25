using System;
using Aprillz.MewUI.Controls;

namespace OpenPredator.Gui;

internal sealed record NavPage(string Title, Func<UIElement> Build);
