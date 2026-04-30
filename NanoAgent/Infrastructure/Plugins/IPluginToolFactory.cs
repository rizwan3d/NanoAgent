using NanoAgent.Application.Abstractions;

namespace NanoAgent.Infrastructure.Plugins;

internal interface IPluginToolFactory
{
    string PluginName { get; }

    IReadOnlyList<ITool> CreateTools(PluginConfiguration configuration);
}
