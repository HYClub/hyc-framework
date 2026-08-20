namespace HYC.Framework.UI
{
    /// <summary>
    /// Tool-tip view that exposes the hot area's data as a strongly-typed
    /// <typeparamref name="T"/>.
    /// </summary>
    public abstract partial class BaseTipView<T> : AbsTipView where T : class
    {
        public T Data
        {
            get
            {
                return mHotArea.GetData() as T;
            }
        }
    }

    /// <summary>
    /// Tool-tip view with a generated <see cref="IComponentBinder"/>.
    /// </summary>
    public abstract partial class BaseTipView<T, K> : BaseTipView<T> where T : class where K : IComponentBinder, new()
    {
        private K mComponentBinder;

        public K Binder
        {
            get
            {
                if (mComponentBinder == null)
                {
                    mComponentBinder = new K();
                    mComponentBinder.Reset(View);
                }

                return mComponentBinder;
            }
        }

        protected override void InitView()
        {
        }

        public override void OnViewClose()
        {
            base.OnViewClose();

            mComponentBinder = default;
        }
    }
}