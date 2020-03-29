﻿using AMQSongProcessor.UI.ViewModels;

using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace AMQSongProcessor.UI.Views
{
	public class AddView : ReactiveUserControl<AddViewModel>
	{
		public AddView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
			=> AvaloniaXamlLoader.Load(this);
	}
}