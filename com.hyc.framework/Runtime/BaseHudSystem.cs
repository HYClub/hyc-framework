namespace HYC.Framework.UI
{
    public abstract partial class BaseHudSystem : AbsUISystem
    {
        public override bool Focusable => false;
    }

    public abstract partial class BaseHudSystem<T> : BaseHudSystem where T : IComponentBinder, new()
    {
        private T mComponentBinder;

        public T Binder
        {
            get
            {
                if (mComponentBinder == null)
                {
                    mComponentBinder = new T();
                    mComponentBinder.Reset(View);
                }

                return mComponentBinder;
            }
        }

        public override void OnViewClose()
        {
            base.OnViewClose();
            mComponentBinder = default;
        }
    }
}