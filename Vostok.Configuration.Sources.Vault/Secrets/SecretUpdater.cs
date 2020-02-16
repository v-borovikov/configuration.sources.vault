﻿using System.Linq;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Model;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Configuration.Sources.Json;
using Vostok.Configuration.Sources.Vault.Helpers;
using Vostok.Logging.Abstractions;

namespace Vostok.Configuration.Sources.Vault.Secrets
{
    internal class SecretUpdater
    {
        private const string SecretPrefix = "secret/";

        private static readonly string[] SecretDataScope = {"data", "data"};

        private readonly VaultSourceState state;
        private readonly IClusterClient client;
        private readonly ILog log;
        private readonly string path;

        public SecretUpdater(VaultSourceState state, IClusterClient client, ILog log, string path)
        {
            this.state = state;
            this.client = client;
            this.log = log;

            if (path.StartsWith(SecretPrefix))
                path = path.Substring(SecretPrefix.Length);

            this.path = path;
        }

        public async Task UpdateAsync()
        {
            var request = Request.Get($"v1/secret/data/{path}").WithToken(state.Token);

            var response = (await client.SendAsync(request, cancellationToken: state.Cancellation).ConfigureAwait(false)).Response;

            var result = new SecretReadResult(response.Code, response.Content.ToString());

            if (result.IsSuccessful || result.IsSecretNotFound)
            {
                if (state.UpdateSecretData(ParseSecretData(result)))
                    log.Info("Updated the secret to a new value.");
            }
            else
            {
                if (!state.IsCanceled)
                    log.Warn("Failed to read secret '{SecretPath}'. Response code = {ResponseCode}.", path, (int)response.Code);

                if (result.IsAccessDenied)
                    state.RenewTokenImmediately();
            }
        }

        private ISettingsNode ParseSecretData(SecretReadResult result)
        {
            if (result.IsSecretNotFound)
                return new ObjectNode(null, Enumerable.Empty<ISettingsNode>());

            try
            {
                return JsonConfigurationParser.Parse(result.Payload).ScopeTo(SecretDataScope);
            }
            catch
            {
                log.Error("Failed to parse secret data from server response.");

                return null;
            }
        }
    }
}
