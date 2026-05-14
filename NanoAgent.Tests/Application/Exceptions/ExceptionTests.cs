using FluentAssertions;
using NanoAgent.Application.Exceptions;

namespace NanoAgent.Tests.Application.Exceptions;

public sealed class ExceptionTests
{
    [Fact]
    public void ConversationPipelineException_Should_Set_Message()
    {
        var ex = new ConversationPipelineException("test error");

        ex.Message.Should().Contain("test error");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void ConversationPipelineException_Should_Set_MessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new ConversationPipelineException("outer", inner);

        ex.Message.Should().Contain("outer");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void ConversationProviderException_Should_Set_Message()
    {
        var ex = new ConversationProviderException("provider error");

        ex.Message.Should().Contain("provider error");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void ConversationProviderException_Should_Set_MessageAndInnerException()
    {
        var inner = new Exception("inner");
        var ex = new ConversationProviderException("outer", inner);

        ex.Message.Should().Contain("outer");
        ex.InnerException.Should().Be(inner);
    }

    [Fact]
    public void ConversationResponseException_Should_Set_Message()
    {
        var ex = new ConversationResponseException("response error");

        ex.Message.Should().Contain("response error");
    }

    [Fact]
    public void ModelDiscoveryException_Should_Set_Message()
    {
        var ex = new ModelDiscoveryException("discovery error");

        ex.Message.Should().Contain("discovery error");
    }

    [Fact]
    public void ModelProviderException_Should_Set_Message()
    {
        var ex = new ModelProviderException("provider error");

        ex.Message.Should().Contain("provider error");
    }

    [Fact]
    public void ModelSelectionException_Should_Set_Message()
    {
        var ex = new ModelSelectionException("selection error");

        ex.Message.Should().Contain("selection error");
    }

    [Fact]
    public void PromptCancelledException_Should_Set_Message()
    {
        var ex = new PromptCancelledException("cancelled");

        ex.Message.Should().Contain("cancelled");
    }

    [Fact]
    public void CodeIntelligenceUnavailableException_Should_Set_Message()
    {
        var ex = new CodeIntelligenceUnavailableException("service unavailable");

        ex.Message.Should().Contain("service unavailable");
    }

    [Fact]
    public void SectionWorkspaceMismatchException_Should_Set_Message()
    {
        var ex = new SectionWorkspaceMismatchException("/current/path", "/section/path");

        ex.Message.Should().Contain("Working directory does not match json's dir.");
        ex.Message.Should().Contain("/current/path");
        ex.Message.Should().Contain("/section/path");
        ex.CurrentWorkspacePath.Should().Be("/current/path");
        ex.SectionWorkspacePath.Should().Be("/section/path");
    }
}
