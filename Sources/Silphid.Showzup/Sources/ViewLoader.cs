﻿using System;
using Silphid.Extensions;
using Silphid.Loadzup;
using UniRx;
using Rx = UniRx;
using UnityEngine;
using Zenject;
using CancellationToken = UniRx.CancellationToken;
using Object = UnityEngine.Object;

namespace Silphid.Showzup
{
    public class ViewLoader : IViewLoader
    {
        private readonly ILoader _loader;
        private readonly IInjector _injector;
        private readonly IViewModelFactory _viewModelFactory;
        private readonly ILogger _logger;

        public ViewLoader(ILoader loader, IInjector injector, IViewModelFactory viewModelFactory, [InjectOptional] ILogger logger = null)
        {
            _loader = loader;
            _injector = injector;
            _viewModelFactory = viewModelFactory;
            _logger = logger;
        }

        public Rx.IObservable<IView> Load(ViewInfo viewInfo, CancellationToken cancellationToken)
        {
            if (viewInfo.View != null)
                return Load(viewInfo.ViewModel, viewInfo.View);

            if (viewInfo.Model != null && viewInfo.ViewModelType != null && viewInfo.ViewType != null && viewInfo.PrefabUri != null)
                return Load(viewInfo.Model, viewInfo.ViewModelType, viewInfo.ViewType, viewInfo.PrefabUri, cancellationToken);

            if (viewInfo.ViewType != null && viewInfo.PrefabUri != null)
                return Load(viewInfo.ViewModel, viewInfo.ViewType, viewInfo.PrefabUri, cancellationToken);

            return Observable.Return<IView>(null);
        }

        private Rx.IObservable<IView> Load(object viewModel, IView view)
        {
            return Observable.Return(view)
                .Do(x => InjectView(x, viewModel))
                .ContinueWith(x => LoadLoadable(x).ThenReturn(view));
        }

        private Rx.IObservable<IView> Load(object model, Type viewModelType, Type viewType, Uri uri, CancellationToken cancellationToken) =>
            Load(_viewModelFactory.Create(model, viewModelType), viewType, uri, cancellationToken);

        private Rx.IObservable<IView> Load(object viewModel, Type viewType, Uri uri, CancellationToken cancellationToken)
        {
            _logger?.Log(nameof(ViewLoader), $"Loading prefab {uri} with {viewType} for {viewModel?.GetType().Name}");
            return LoadPrefabView(viewType, uri, cancellationToken)
                .Do(view => InjectView(view, viewModel))
                .ContinueWith(view => LoadLoadable(view).ThenReturn(view));
        }

        private void InjectView(IView view, object viewModel)
        {
            view.ViewModel = viewModel;
            _logger?.Log(nameof(ViewLoader), $"Initializing {view} with ViewModel {viewModel}");
            _injector.Inject(view.GameObject);
        }

        private Rx.IObservable<Unit> LoadLoadable(IView view) =>
            (view as ILoadable)?.Load() ?? Observable.ReturnUnit();

        #region Prefab view loading

        private Rx.IObservable<IView> LoadPrefabView(Type viewType, Uri uri, CancellationToken cancellationToken)
        {
            _logger?.Log(nameof(ViewLoader), $"LoadPrefabView({viewType}, {uri})");

            return _loader.Load<GameObject>(uri)
                .Last()
                .Where(obj => CheckCancellation(cancellationToken))
                .Select(x => Instantiate(x, cancellationToken))
                .WhereNotNull()
                .DoOnError(ex => _logger?.LogError(nameof(ViewLoader),
                    $"Failed to load view {viewType} from {uri} with error:{Environment.NewLine}{ex}"))
                .Select(x => GetViewFromPrefab(x, viewType));
        }

        private bool CheckCancellation(CancellationToken cancellationToken, Action cancellationAction = null)
        {
            if (!cancellationToken.IsCancellationRequested)
                return true;

            cancellationAction?.Invoke();
            return false;
        }

        private GameObject Instantiate(GameObject original, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

#if UNITY_EDITOR
            original = Object.Instantiate(original);
#endif

            DisableAllViews(original);

            var instance = Object.Instantiate(original);
#if UNITY_EDITOR
            Object.Destroy(original);
#endif

            if (cancellationToken.IsCancellationRequested)
            {
                instance.Destroy();
                return null;
            }

            return instance;
        }

        private void DisableAllViews(GameObject obj)
        {
            obj.GetComponents<IView>().ForEach(x => x.IsActive = false);
        }

        private IView GetViewFromPrefab(GameObject gameObject, Type viewType)
        {
            var view = (IView) gameObject.GetComponent(viewType);

            if (view == null)
                throw new InvalidOperationException(
                    $"Loaded prefab {gameObject.name} has no view component of type {viewType.Name}");

            return view;
        }

        #endregion
    }
}