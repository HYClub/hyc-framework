using System;

namespace HYC.Framework.Config
{
    /// <summary>
    /// Marks a struct as a generated configuration record that the config
    /// pipeline will bake into a BlobAsset table.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class BlobGenerateAttribute : Attribute
    {
        public string SheetName { get; }
        public BlobGenerateAttribute(string sheetName = null) => SheetName = sheetName;
    }

    /// <summary>
    /// Marks the runtime wrapper that exposes a generated config table entry
    /// (e.g. <c>CfgXxxWrapper</c>) so the editor tooling can pair struct + wrapper.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class CfgWrapperAttribute : Attribute
    {
        public Type WrapperOf { get; }
        public CfgWrapperAttribute(Type wrapperOf) => WrapperOf = wrapperOf;
    }

    /// <summary>
    /// Marks a struct that the config generator must ALSO produce an
    /// entity component from (baking path for spawns).
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class ComponentGenerateAttribute : Attribute
    {
        public bool AsBlob { get; }
        public ComponentGenerateAttribute(bool asBlob = false) => AsBlob = asBlob;
    }

    /// <summary>
    /// Instructs the config pipeline not to auto-rename fields during code
    /// regeneration (e.g. acronyms already matching game-side naming).
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class | AttributeTargets.Field, AllowMultiple = false)]
    public class DoNotRenameCfgAttribute : Attribute
    {
    }
}