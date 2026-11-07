using System;
using DE.Server.NativeBridge;
using DE.Server.Stubs;

namespace DE.Server.Entities
{
    public partial class ServerSpaceEntity
    {
        private ulong _playerCountReportTimerId;

        private void StartStatistics()
        {
            if (_playerCountReportTimerId == 0)
            {
                _playerCountReportTimerId = DETimer.AddTimer(TimeSpan.FromMinutes(1), ReportStatistics, true);
            }
        }

        private void StopStatistics()
        {
            if (_playerCountReportTimerId != 0)
            {
                DETimer.CancelTimer(_playerCountReportTimerId);
                _playerCountReportTimerId = 0;
            }
        }

        private void ReportStatistics()
        {
            var reportTimeMilliseconds = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ServerStubCaller.Call(nameof(SpaceStub), nameof(SpaceStub.ReportSpaceStat), MailBox, SpaceNo,
                reportTimeMilliseconds, Avatars.Count);
        }
    }
}
