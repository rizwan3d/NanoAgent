using StemCode.Application.Models;

namespace StemCode.Application.Commands;

public interface IReplCommandParser
{
    ParsedReplCommand Parse(string commandText);
}
