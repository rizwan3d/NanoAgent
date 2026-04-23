using NanoAgent.Application.Models;
using NanoAgent.Presentation.Abstractions;

namespace NanoAgent.Presentation.Abstractions;

public interface IReplCommandParser
{
    ParsedReplCommand Parse(string commandText);
}
