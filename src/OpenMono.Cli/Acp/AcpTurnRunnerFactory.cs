namespace OpenMono.Acp;

/// <summary>
/// Constructs <see cref="AcpTurnRunner"/> instances. Resolved via DI by
/// <see cref="AcpEndpoints"/> on every <c>POST /api/v1/sessions/:id/turn</c>
/// so each request gets its own runner bound to its own <see cref="SseWriter"/>.
/// </summary>
public sealed class AcpTurnRunnerFactory
{
    private readonly ConversationLoopFactory _loopFactory;
    private readonly AcpServerSettings _settings;

    public AcpTurnRunnerFactory(ConversationLoopFactory loopFactory, AcpServerSettings settings)
    {
        _loopFactory = loopFactory;
        _settings = settings;
    }

    public AcpTurnRunner Create(AcpSession session, SseWriter writer)
        => new AcpTurnRunner(session, writer, _loopFactory, _settings);
}
