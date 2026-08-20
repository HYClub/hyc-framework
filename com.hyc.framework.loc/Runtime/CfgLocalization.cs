using Unity.Entities;

namespace HYC.Framework.Loc
{
    /// <summary>Serialized localization value array (one file per language).</summary>
    public struct CfgLocalization
    {
        public BlobArray<BlobString> values;
    }

    /// <summary>Serialized index array (maps localization ids to source Excel files).</summary>
    public struct CfgLocalizationIndex
    {
        public BlobArray<int> values;
    }
}
