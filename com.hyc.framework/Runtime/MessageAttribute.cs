using System;

namespace HYC.Framework.Dots
{
    /// <summary>
    /// Base contract shared by attributes that describe one ECS "topic"
    /// (messages and statuses). Exposes the metadata the editor component
    /// browser and validation tooling reflect over.
    /// </summary>
    public interface IBaseAttribute
    {
        string Desc { get; }
        string Group { get; }
        int Order { get; }
    }

    /// <summary>
    /// Marks a component (struct) as a fire-and-forget message. Messages are
    /// created on an entity tagged <see cref="Message"/> and consumed during
    /// the same frame, then cleared by <see cref="MessageExpirySystem"/>.
    /// The desc/group/order metadata powers the editor component browser.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class MessageAttribute : Attribute, IBaseAttribute
    {
        private readonly string _desc;
        private readonly string _group;
        private readonly int _order;

        public string Desc => _desc;
        public string Group => _group;
        public int Order => _order;

        public MessageAttribute(string desc) => _desc = desc;

        public MessageAttribute(string desc, string group) : this(desc) => _group = group;

        public MessageAttribute(string desc, string group, int order) : this(desc, group) => _order = order;
    }

    /// <summary>
    /// Marks a component (struct) as a persistent status/state that lives on
    /// an entity while it is in effect. Same metadata contract as
    /// <see cref="MessageAttribute"/> so the browser can list both.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class StatusAttribute : Attribute, IBaseAttribute
    {
        private readonly string _desc;
        private readonly string _group;
        private readonly int _order;

        public string Desc => _desc;
        public string Group => _group;
        public int Order => _order;

        public StatusAttribute(string desc) => _desc = desc;

        public StatusAttribute(string desc, string group) : this(desc) => _group = group;

        public StatusAttribute(string desc, string group, int order) : this(desc, group) => _order = order;
    }
}
