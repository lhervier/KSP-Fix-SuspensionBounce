using System;

namespace com.github.lhervier.ksp.fix.suspensions
{
    /// <summary>
    /// Marks a method that KSP-Util-MCPServer, when it is installed, offers as a tool. The server finds it by the
    /// attribute's name and reads <see cref="Name"/> and <see cref="Description"/>: this mod does not depend on it, and
    /// runs the same without it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class McpToolAttribute : Attribute
    {
        /// <summary>The tool's name.</summary>
        public string Name { get; }

        /// <summary>What the tool does, for whoever calls it.</summary>
        public string Description { get; }

        public McpToolAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}
