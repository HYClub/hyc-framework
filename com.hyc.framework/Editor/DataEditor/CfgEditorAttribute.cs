using System;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// Maps a config asset type to a custom editor class derived from
    /// <see cref="ConfigDataEditor"/>. Without it the data editor falls back to
    /// <see cref="NormalConfigEditor"/>, which draws every serialized field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class CfgEditorAttribute : Attribute
    {
        public Type Type;

        public CfgEditorAttribute(Type type)
        {
            Type = type;
        }
    }
}
