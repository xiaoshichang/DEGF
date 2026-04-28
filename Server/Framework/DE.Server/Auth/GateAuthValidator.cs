using System;
using System.Collections.Generic;
using DE.Share.Utils;

namespace DE.Server.Auth
{
    public sealed class GateAuthValidationRequest
    {
        public string Account { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public List<string> GateServerIds { get; set; } = new List<string>();
    }

    public sealed class GateAuthValidationResult
    {
        public bool IsSuccess { get; set; }
        public int StatusCode { get; set; }
        public string Error { get; set; } = string.Empty;
        public string ExpectedServerId { get; set; } = string.Empty;
    }

    public static class GateAuthValidator
    {
        public static GateAuthValidationResult Validate(
            string currentServerId,
            string account,
            string password,
            IReadOnlyList<string> gateServerIds)
        {
            if (string.IsNullOrWhiteSpace(currentServerId))
            {
                return new GateAuthValidationResult
                {
                    StatusCode = 503,
                    Error = "server id unavailable",
                };
            }

            string normalizedAccount = account == null ? string.Empty : account.Trim();
            string normalizedPassword = password == null ? string.Empty : password.Trim();
            if (string.IsNullOrEmpty(normalizedAccount) || string.IsNullOrEmpty(normalizedPassword))
            {
                return new GateAuthValidationResult
                {
                    StatusCode = 401,
                    Error = "invalid account or password",
                };
            }

            string targetGateServerId = GateSelector.SelectTargetGateServerId(normalizedAccount, gateServerIds);
            if (string.IsNullOrEmpty(targetGateServerId))
            {
                return new GateAuthValidationResult
                {
                    StatusCode = 503,
                    Error = "gate routing unavailable",
                };
            }

            if (!string.Equals(targetGateServerId, currentServerId, StringComparison.Ordinal))
            {
                return new GateAuthValidationResult
                {
                    StatusCode = 403,
                    Error = "account routed to another gate",
                    ExpectedServerId = targetGateServerId,
                };
            }

            return new GateAuthValidationResult
            {
                IsSuccess = true,
                StatusCode = 200,
            };
        }
    }
}
