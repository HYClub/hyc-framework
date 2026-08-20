namespace HYC.Framework.UI
{
    public abstract partial class BaseDialogSystem : AbsUISystem
    {
        public override bool Focusable => true;
    }

    public abstract partial class BaseDialogSystem<T> : BaseDialogSystem where T : IComponentBinder, new()
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