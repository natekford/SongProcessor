﻿using Avalonia.Input.Platform;

using DynamicData;

using ReactiveUI;

using SongProcessor.FFmpeg;
using SongProcessor.Gatherers;

using Splat;

using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace SongProcessor.UI.ViewModels;

[DataContract]
public sealed class MainViewModel : ReactiveObject, IScreen
{
	[DataMember]
	public RoutingState Router
	{
		get;
		// Due to certain design choices I have made, changing the RoutingState
		// instance causes routing to not work. Buttons can be clicked which
		// causes the NavigationStack to update but the UI does not.
		init
		{
			field.NavigationStack.Clear();
			field.NavigationStack.AddRange(value?.NavigationStack ?? []);
			this.RaisePropertyChanged(nameof(Router));
		}
	} = new();

	#region Commands
	public ReactiveCommand<Unit, Unit> Add { get; }
	public ReactiveCommand<Unit, Unit> GoBack { get; }
	public ReactiveCommand<Unit, Unit> Load { get; }
	#endregion Commands

	public MainViewModel(
		ISongLoader loader,
		ISongProcessor processor,
		ISourceInfoGatherer gatherer,
		IClipboard clipboard,
		IMessageBoxManager messageBoxManager,
		IEnumerable<IAnimeGatherer> gatherers)
	{
		Load = CreateViewModelCommand(() => new SongViewModel(
			this,
			loader,
			processor,
			gatherer,
			clipboard,
			messageBoxManager
		));
		Add = CreateViewModelCommand(() => new AddViewModel(
			this,
			loader,
			messageBoxManager,
			gatherers
		));
		GoBack = ReactiveCommand.Create(() =>
		{
			Router.NavigateBack.Execute();
		}, CanGoBack());
	}

	[JsonConstructor]
	private MainViewModel() : this(
		Locator.Current.GetService<ISongLoader>()!,
		Locator.Current.GetService<ISongProcessor>()!,
		Locator.Current.GetService<ISourceInfoGatherer>()!,
		Locator.Current.GetService<IClipboard>()!,
		Locator.Current.GetService<IMessageBoxManager>()!,
		Locator.Current.GetService<IEnumerable<IAnimeGatherer>>()!)
	{
	}

	private IObservable<bool> CanGoBack()
	{
		var notSingle = this
			.WhenAnyValue(x => x.Router.NavigationStack.Count)
			.Select(x => x > 0);
		return CanNavigate().CombineLatest(notSingle, (x, y) => x && y);
	}

	private IObservable<bool> CanNavigate()
	{
		return this
			.WhenAnyObservable(x => x.Router.CurrentViewModel)
			.SelectMany(x =>
			{
				if (x is INavigationController controller)
				{
					return controller.CanNavigate;
				}
				return Observable.Return(true);
			});
	}

	private IObservable<bool> CanNavigateTo<T>()
		where T : IRoutableViewModel
	{
		var isDifferent = this
			.WhenAnyObservable(x => x.Router.CurrentViewModel)
			.Select(x => x is not T);
		return CanNavigate().CombineLatest(isDifferent, (x, y) => x && y);
	}

	private ReactiveCommand<Unit, Unit> CreateViewModelCommand<T>(Func<T> factory)
		where T : IRoutableViewModel
	{
		return ReactiveCommand.Create(() =>
		{
			Router.Navigate.Execute(factory());
		}, CanNavigateTo<T>());
	}
}