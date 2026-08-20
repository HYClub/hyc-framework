using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Base class for data editor panes. A pane is created per selected asset
    /// type by <see cref="ConfigDataContainer"/>; the default
    /// <see cref="NormalConfigEditor"/> simply draws all serialized fields plus
    /// an optional preview. Games can derive and annotate with
    /// <see cref="CfgEditorAttribute"/> for bespoke layouts.
    /// </summary>
    public abstract class ConfigDataEditor
    {
        protected ConfigDataWindow mWindow;
        protected TreeView mTree;
        protected ConfigDataTreeNode mTreeNode;
        protected SerializedObject mTarget;

        /// <summary>Called when the pane is first attached to an asset.</summary>
        public virtual void Open(ConfigDataWindow window, TreeView tree, ConfigDataTreeNode treeNode, SerializedObject so)
        {
            mWindow = window;
            mTree = tree;
            mTreeNode = treeNode;
            mTarget = so;

            Init();
        }

        protected virtual void Init()
        {
        }

        public virtual void Reload()
        {
        }

        public virtual void OnGUI(float viewW, float viewH)
        {
        }

        public virtual void Dispose()
        {
            mWindow = null;
            mTree = null;
            mTreeNode = null;
            mTarget = null;
        }
    }
}
