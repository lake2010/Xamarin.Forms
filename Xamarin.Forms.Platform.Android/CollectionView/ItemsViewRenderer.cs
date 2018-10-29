using System;
using System.ComponentModel;
using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.Support.V7.Widget;
using Android.Util;
using Android.Views;
using Android.Widget;
using Xamarin.Forms.Internals;
using Xamarin.Forms.Platform.Android.FastRenderers;
using AView = Android.Views.View;

namespace Xamarin.Forms.Platform.Android
{
	public class ItemsViewRenderer : RecyclerView, IVisualElementRenderer, IEffectControlProvider
	{
		readonly AutomationPropertiesProvider _automationPropertiesProvider;
		readonly EffectControlProvider _effectControlProvider;

		protected ItemsViewAdapter ItemsViewAdapter;
		
		int? _defaultLabelFor;
		bool _disposed;
		protected ItemsView ItemsView;
		IItemsLayout _layout;
		SnapManager _snapManager;
		ScrollHelper _scrollHelper;

		EmptyViewAdapter _emptyViewAdapter;
		DataChangeObserver _dataChangeViewObserver;

		public ItemsViewRenderer(Context context) : base(context)
		{
			if (!Forms.Flags.Contains(Flags.CollectionViewExperimental))
			{
				var collectionViewFlagError = 
					$"To use CollectionView on this platform, you must opt-in by calling " 
					+ $"Forms.SetFlags(\"{Flags.CollectionViewExperimental}\") before Forms.Init().";
				throw new InvalidOperationException(collectionViewFlagError);
			}

			_automationPropertiesProvider = new AutomationPropertiesProvider(this);
			_effectControlProvider = new EffectControlProvider(this);
		}

		ScrollHelper ScrollHelper => _scrollHelper ?? (_scrollHelper = new ScrollHelper(this));

		// TODO hartez 2018/10/24 19:27:12 Region all the interface implementations	

		protected override void OnLayout(bool changed, int l, int t, int r, int b)
		{
			base.OnLayout(changed, l, t, r, b);
			ClipBounds = new Rect(0,0, Width, Height);

			// After a direct (non-animated) scroll operation, we may need to make adjustments
			// to align the target item; if an adjustment is pending, execute it here.
			// (Deliberately checking the private member here rather than the property accessor; the accessor will
			// create a new ScrollHelper if needed, and there's no reason to do that until a Scroll is requested.)
			_scrollHelper?.AdjustScroll();
		}

		void IEffectControlProvider.RegisterEffect(Effect effect)
		{
			_effectControlProvider.RegisterEffect(effect);
		}

		public VisualElement Element => ItemsView;

		public event EventHandler<VisualElementChangedEventArgs> ElementChanged;

		public event EventHandler<PropertyChangedEventArgs> ElementPropertyChanged;

		SizeRequest IVisualElementRenderer.GetDesiredSize(int widthConstraint, int heightConstraint)
		{
			Measure(widthConstraint, heightConstraint);
			return new SizeRequest(new Size(MeasuredWidth, MeasuredHeight), new Size());
		}

		void IVisualElementRenderer.SetElement(VisualElement element)
		{
			if (element == null)
			{
				throw new ArgumentNullException(nameof(element));
			}

			if (!(element is ItemsView))
			{
				throw new ArgumentException($"{nameof(element)} must be of type {nameof(ItemsView)}");
			}

			Performance.Start(out string perfRef);

			VisualElement oldElement = ItemsView;
			ItemsView = (ItemsView)element;

			OnElementChanged(oldElement as ItemsView, ItemsView);

			// TODO hartez 2018/06/06 20:57:12 Find out what this does, and whether we really need it	
			element.SendViewInitialized(this);

			Performance.Stop(perfRef);
		}

		void IVisualElementRenderer.SetLabelFor(int? id)
		{
			// TODO hartez 2018/06/06 20:58:54 Rethink whether we need to have _defaultLabelFor as a class member	
			if (_defaultLabelFor == null)
			{
				_defaultLabelFor = LabelFor;
			}

			LabelFor = (int)(id ?? _defaultLabelFor);
		}

		public VisualElementTracker Tracker { get; private set; }

		void IVisualElementRenderer.UpdateLayout()
		{
			Tracker?.UpdateLayout();
		}

		public global::Android.Views.View View => this;

		public ViewGroup ViewGroup => null;

		protected override void Dispose(bool disposing)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			if (disposing)
			{
				_automationPropertiesProvider?.Dispose();
				Tracker?.Dispose();

				if (Element != null)
				{
					TearDownOldElement(Element as ItemsView);

					if (Platform.GetRenderer(Element) == this)
					{
						Element.ClearValue(Platform.RendererProperty);
					}
				}
			}

			base.Dispose(disposing);
		}

		protected virtual LayoutManager SelectLayoutManager(IItemsLayout layoutSpecification)
		{
			switch (layoutSpecification)
			{
				case GridItemsLayout gridItemsLayout:
					return CreateGridLayout(gridItemsLayout);
				case ListItemsLayout listItemsLayout:
					var orientation = listItemsLayout.Orientation == ItemsLayoutOrientation.Horizontal
						? LinearLayoutManager.Horizontal
						: LinearLayoutManager.Vertical;

					return new LinearLayoutManager(Context, orientation, false);
			}

			// Fall back to plain old vertical list
			// TODO hartez 2018/08/30 19:34:36 Log a warning when we have to fall back because of an unknown layout	
			return new LinearLayoutManager(Context);
		}

		GridLayoutManager CreateGridLayout(GridItemsLayout gridItemsLayout)
		{
			return new GridLayoutManager(Context, gridItemsLayout.Span,
				gridItemsLayout.Orientation == ItemsLayoutOrientation.Horizontal
					? LinearLayoutManager.Horizontal
					: LinearLayoutManager.Vertical,
				false);
		}

		void OnElementChanged(ItemsView oldElement, ItemsView newElement)
		{
			TearDownOldElement(oldElement);
			SetUpNewElement(newElement);

			ElementChanged?.Invoke(this, new VisualElementChangedEventArgs(oldElement, newElement));

			EffectUtilities.RegisterEffectControlProvider(this, oldElement, newElement);
			
			UpdateBackgroundColor();
			UpdateFlowDirection();
		}

		void OnElementPropertyChanged(object sender, PropertyChangedEventArgs changedProperty)
		{
			ElementPropertyChanged?.Invoke(this, changedProperty);

			// TODO hartez 2018/10/24 10:41:55 If the ItemTemplate changes between set and null, we need to make sure to clear the recyclerview pool	

			if (changedProperty.Is(ItemsView.ItemsSourceProperty))
			{
				UpdateItemsSource();
			}
			else if (changedProperty.Is(VisualElement.BackgroundColorProperty))
			{
				UpdateBackgroundColor();
			}
			else if (changedProperty.Is(VisualElement.FlowDirectionProperty))
			{
				UpdateFlowDirection();
			}
			else if (changedProperty.IsOneOf(ItemsView.EmptyViewProperty, ItemsView.EmptyViewTemplateProperty))
			{
				UpdateEmptyView();
			}
		}

		public override ViewHolder FindViewHolderForAdapterPosition(int position)
		{
			System.Diagnostics.Debug.WriteLine($">>>>> ItemsViewRenderer FindViewHolderForAdapterPosition 217: MESSAGE");
			return base.FindViewHolderForAdapterPosition(position);
		}

		public override ViewHolder FindViewHolderForLayoutPosition(int position)
		{
			System.Diagnostics.Debug.WriteLine($">>>>> ItemsViewRenderer FindViewHolderForLayoutPosition 223: MESSAGE");
			
			return base.FindViewHolderForLayoutPosition(position);
		}

		public override ViewHolder FindViewHolderForItemId(long id)
		{
			System.Diagnostics.Debug.WriteLine($">>>>> ItemsViewRenderer FindViewHolderForItemId 230: MESSAGE");
			return base.FindViewHolderForItemId(id);
		}

		public override ViewHolder FindContainingViewHolder(AView view)
		{
			System.Diagnostics.Debug.WriteLine($">>>>> ItemsViewRenderer FindContainingViewHolder 217: {view.GetType()}");
			return base.FindContainingViewHolder(view);
		}

		protected virtual void UpdateItemsSource()
		{
			if (ItemsView == null)
			{
				return;
			}

			// Stop watching the old adapter to see if it's empty (if we _are_ watching)
			Unwatch(GetAdapter());
			
			ItemsViewAdapter = new ItemsViewAdapter(ItemsView, Context);
			SwapAdapter(ItemsViewAdapter, false);

			UpdateEmptyView();
		}

		void Unwatch(RecyclerView.Adapter adapter)
		{
			if (adapter != null && _dataChangeViewObserver != null)
			{
				adapter.UnregisterAdapterDataObserver(_dataChangeViewObserver);
			}
		}

		// TODO hartez 2018/10/24 19:25:14 I don't like these method names; too generic 	
		void Watch(RecyclerView.Adapter adapter)
		{
			if (_dataChangeViewObserver == null)
			{
				_dataChangeViewObserver = new DataChangeObserver(UpdateEmptyViewVisibility);
			}

			adapter.RegisterAdapterDataObserver(_dataChangeViewObserver);
		}

		void SetUpNewElement(ItemsView newElement)
		{
			if (newElement == null)
			{
				return;
			}

			newElement.PropertyChanged += OnElementPropertyChanged;

			// TODO hartez 2018/06/06 20:49:14 Review whether we can just do this in the constructor	
			if (Tracker == null)
			{
				Tracker = new VisualElementTracker(this);
			}

			this.EnsureId();

			UpdateItemsSource();

			_layout = newElement.ItemsLayout;
			SetLayoutManager(SelectLayoutManager(_layout));
			UpdateSnapBehavior();

			// Keep track of the ItemsLayout's property changes
			_layout.PropertyChanged += LayoutOnPropertyChanged;

			// TODO hartez 2018/09/17 13:16:12 This propertychanged handler needs to be torn down in Dispose and TearDownElement	

			// Listen for ScrollTo requests
			newElement.ScrollToRequested += ScrollToRequested;
		}
		
		void TearDownOldElement(ItemsView oldElement)
		{
			if (oldElement == null)
			{
				return;
			}

			// Stop listening for property changes
			oldElement.PropertyChanged -= OnElementPropertyChanged;

			// Stop listening for ScrollTo requests
			oldElement.ScrollToRequested -= ScrollToRequested;

			var adapter = GetAdapter();

			if (adapter != null)
			{
				adapter.Dispose();
				SetAdapter(null);
			}

			if (_snapManager != null)
			{
				_snapManager.Dispose();
				_snapManager = null;
			}
		}

		void LayoutOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChanged)
		{
			if(propertyChanged.Is(GridItemsLayout.SpanProperty))
			{
				if (GetLayoutManager() is GridLayoutManager gridLayoutManager)
				{
					gridLayoutManager.SpanCount = ((GridItemsLayout)_layout).Span;
				}
			} 
			else if (propertyChanged.IsOneOf(ItemsLayout.SnapPointsTypeProperty, ItemsLayout.SnapPointsAlignmentProperty))
			{
				UpdateSnapBehavior();
			}
		}

		protected virtual void UpdateSnapBehavior()
		{
			if (_snapManager == null)
			{
				_snapManager = new SnapManager(ItemsView, this);
			}

			_snapManager.UpdateSnapBehavior();
		}

		// TODO hartez 2018/08/09 09:30:17 Package up background color and flow direction providers so we don't have to re-implement them here	
		protected virtual void UpdateBackgroundColor(Color? color = null)
		{
			if (Element == null)
			{
				return;
			}

			SetBackgroundColor((color ?? Element.BackgroundColor).ToAndroid());
		}

		protected virtual void UpdateFlowDirection()
		{
			if (Element == null)
			{
				return;
			}

			this.UpdateFlowDirection(Element);

			ReconcileFlowDirectionAndLayout();
		}

		protected virtual void UpdateEmptyView()
		{
			if (ItemsViewAdapter == null)
			{
				return;
			}

			var emptyView = ItemsView?.EmptyView;
			var emptyViewTemplate = ItemsView?.EmptyViewTemplate;

			if (emptyView != null || emptyViewTemplate != null)
			{
				if (_emptyViewAdapter == null)
				{
					_emptyViewAdapter = new EmptyViewAdapter();
				}

				_emptyViewAdapter.EmptyView = emptyView;
				_emptyViewAdapter.EmptyViewTemplate = emptyViewTemplate;
				Watch(ItemsViewAdapter);
			}
			else
			{
				Unwatch(ItemsViewAdapter);
			}

			UpdateEmptyViewVisibility();
		}

		protected virtual void ReconcileFlowDirectionAndLayout()
		{
			if (!(GetLayoutManager() is LinearLayoutManager linearLayoutManager))
			{
				return;
			}

			if (linearLayoutManager.CanScrollVertically())
			{
				return;
			}

			var effectiveFlowDirection = ((IVisualElementController)Element).EffectiveFlowDirection;

			if (effectiveFlowDirection.IsRightToLeft() && !linearLayoutManager.ReverseLayout)
			{
				linearLayoutManager.ReverseLayout = true;
				return;
			}

			if (effectiveFlowDirection.IsLeftToRight() && linearLayoutManager.ReverseLayout)
			{
				linearLayoutManager.ReverseLayout = false;
			}
		}

		protected virtual int DeterminePosition(ScrollToRequestEventArgs args)
		{
			if (args.Mode == ScrollToMode.Position)
			{
				// TODO hartez 2018/08/28 15:40:03 Need to handle group indices here as well	
				return args.Index;
			}

			return ItemsViewAdapter.GetPositionForItem(args.Item);
		}

		void ScrollToRequested(object sender, ScrollToRequestEventArgs args)
		{
			ScrollTo(args);
		}

		protected virtual void ScrollTo(ScrollToRequestEventArgs args)
		{
			var position = DeterminePosition(args);
			
			if (args.Animate)
			{
				ScrollHelper.AnimateScrollToPosition(position, args.ScrollToPosition);
			}
			else
			{
				ScrollHelper.JumpScrollToPosition(position, args.ScrollToPosition);
			}
		}

		internal void UpdateEmptyViewVisibility()
		{
			if (ItemsViewAdapter == null)
			{
				return;
			}

			var showEmptyView = ItemsView?.EmptyView != null && ItemsViewAdapter.ItemCount == 0;

			if (showEmptyView)
			{
				SwapAdapter(_emptyViewAdapter, true);

				// TODO hartez 2018/10/24 17:34:36 If this works, cache this layout manager as _emptyLayoutManager	
				SetLayoutManager(new LinearLayoutManager(Context));
			}
			else
			{
				SwapAdapter(ItemsViewAdapter, true);
				SetLayoutManager(SelectLayoutManager(_layout));
			}
		}
	}
}