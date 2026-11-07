using DE.Server.NativeBridge;
using DE.Share.Rpc;

namespace DE.Server.Stubs
{
    public partial class SpaceStub
    {
        [ServerRpc]
        public void ReportSpaceStat(EntityMailBox mailbox, int spaceNo, ulong reportTimeMilliseconds, int playerCount)
        {
            if (State == SpaceStubState.Shutdown)
            {
                DELogger.Error(nameof(SpaceStub), $"Space statistics rejected because the Stub is stopped, spaceId={mailbox.EntityId}.");
                return;
            }
            if (!mailbox.IsValid)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space statistics contain an invalid Mailbox, spaceId={mailbox.EntityId}, game={mailbox.BindingGame}.");
                return;
            }
            if (playerCount < 0)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space statistics contain a negative player count, spaceId={mailbox.EntityId}, playerCount={playerCount}.");
                return;
            }
            if (reportTimeMilliseconds == 0)
            {
                DELogger.Error(nameof(SpaceStub), $"Space statistics contain a zero report time, spaceId={mailbox.EntityId}.");
                return;
            }
            if (!_spaceProxiesById.TryGetValue(mailbox.EntityId, out var spaceProxy))
            {
                DELogger.Error(nameof(SpaceStub), $"Space statistics refer to an unknown Space proxy, spaceId={mailbox.EntityId}, spaceNo={spaceNo}.");
                return;
            }
            if (!spaceProxy.IsReady)
            {
                DELogger.Error(nameof(SpaceStub), $"Space statistics arrived before creation was confirmed, spaceId={mailbox.EntityId}.");
                return;
            }
            if (spaceProxy.SpaceNo != spaceNo)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space statistics have a mismatched SpaceNo, spaceId={mailbox.EntityId}, expectedSpaceNo={spaceProxy.SpaceNo}, spaceNo={spaceNo}.");
                return;
            }
            if (spaceProxy.MailBox.BindingGame != mailbox.BindingGame)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space statistics have a mismatched host, spaceId={mailbox.EntityId}, "
                    + $"expectedGame={spaceProxy.MailBox.BindingGame}, game={mailbox.BindingGame}.");
                return;
            }
            if (reportTimeMilliseconds < spaceProxy.ReportTimeMilliseconds)
            {
                DELogger.Error(nameof(SpaceStub),
                    $"Space statistics are older than the stored report, spaceId={mailbox.EntityId}, "
                    + $"reportTimeMilliseconds={reportTimeMilliseconds}, storedReportTimeMilliseconds={spaceProxy.ReportTimeMilliseconds}.");
                return;
            }

            spaceProxy.PlayerCount = playerCount;
            spaceProxy.ReportTimeMilliseconds = reportTimeMilliseconds;
        }
    }
}
