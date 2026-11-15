using System;
using DE.Server.NativeBridge;
using DE.Server.Stubs;

namespace DE.Server.Entities
{
    public class ServerSpaceStatisticsComponent : ServerEntityComponent
    {
        private ServerSpaceEntity Space => (ServerSpaceEntity)OwnerEntity;
        private ulong _playerCountReportTimerId;

        /// <summary>启动每分钟人数上报定时器。</summary>
        protected override void Initialize()
        {
            base.Initialize();
            _playerCountReportTimerId = DETimer.AddTimer(TimeSpan.FromMinutes(1), ReportStatistics, true);
        }

        /// <summary>在空间资源释放前停止人数上报定时器。</summary>
        protected override void PreUnInitialize()
        {
            StopStatistics();
            base.PreUnInitialize();
        }

        private void StopStatistics()
        {
            if (_playerCountReportTimerId != 0)
            {
                DETimer.CancelTimer(_playerCountReportTimerId);
                _playerCountReportTimerId = 0;
            }
        }

        internal void ReportStatistics()
        {
            var reportTimeMilliseconds = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            ServerStubCaller.Call(nameof(SpaceStub), nameof(SpaceStub.ReportSpaceStat), Space.MailBox, Space.SpaceNo,
                reportTimeMilliseconds, Space.AvatarManageCmpt.Avatars.Count);
        }
    }
}
