using System;
using System.Collections.Generic;
using DE.Server.Auth;

namespace DE.Server.NativeBridge
{
    public sealed class GateServerRuntimeState
    {
        private readonly ManagedRuntimeState _managedRuntimeState;

        public GateServerRuntimeState(ManagedRuntimeState managedRuntimeState)
        {
            _managedRuntimeState = managedRuntimeState ?? throw new ArgumentNullException(nameof(managedRuntimeState));
        }

        public GateAuthValidationResult ValidateAuth(GateAuthValidationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            IReadOnlyList<string> gateServerIds = request.GateServerIds;
            if (gateServerIds == null)
            {
                gateServerIds = Array.Empty<string>();
            }

            return GateAuthValidator.Validate(
                _managedRuntimeState.ServerId,
                request.Account,
                request.Password,
                gateServerIds
            );
        }

        public void Uninitialize()
        {
        }
    }
}
