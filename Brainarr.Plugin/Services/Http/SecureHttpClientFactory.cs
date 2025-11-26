using System;
using System.Net.Http;
using Brainarr.Plugin.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Http
{
    public static class SecureHttpClientFactory
    {
        public static HttpClient Create(string origin)
        {
            var handler = CertificateValidator.CreateSecureHandler(enableCertificatePinning: false);
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            if (string.Equals(origin, Configuration.Policy.Providers.MusicBrainz, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(origin, "musicbrainz", StringComparison.OrdinalIgnoreCase))
            {
                ConfigureMusicBrainz(client);
            }

            return client;
        }

        private static void ConfigureMusicBrainz(HttpClient client)
        {
            try
            {
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(global::Brainarr.Plugin.Services.Security.UserAgentHelper.Build());
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            }
            catch (Exception) { /* Non-critical */ }
        }
    }
}
