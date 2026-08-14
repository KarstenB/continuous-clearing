// --------------------------------------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: 2025 Siemens AG
//
//  SPDX-License-Identifier: MIT
// -------------------------------------------------------------------------------------------------------------------- 

using log4net;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace SIT.Common
{
    /// <summary>
    /// Applies the optional GITHUB_TOKEN to requests against GitHub, which raises the API rate limit
    /// from 60 to 5000 requests per hour and allows access to private repositories.
    /// </summary>
    public static class GithubAuthHelper
    {
        static readonly ILog Logger = LoggerFactory.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Name of the environment variable that carries the GitHub token.
        /// </summary>
        public const string GithubTokenEnvironmentVariable = "GITHUB_TOKEN";

        private static readonly string[] GithubHosts =
        {
            "github.com",
            "api.github.com",
            "raw.githubusercontent.com",
            "codeload.github.com",
            "objects.githubusercontent.com"
        };

        /// <summary>
        /// Gets the configured GitHub token, or an empty string when the environment variable is not set.
        /// </summary>
        /// <returns>The token value or an empty string.</returns>
        public static string GetToken()
        {
            return Environment.GetEnvironmentVariable(GithubTokenEnvironmentVariable) ?? string.Empty;
        }

        /// <summary>
        /// Determines whether the given URL points at a GitHub host.
        /// </summary>
        /// <param name="url">Absolute URL to inspect.</param>
        /// <returns>True when the host belongs to GitHub.</returns>
        public static bool IsGithubUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            return Array.Exists(GithubHosts, host => string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Adds the bearer authorization header to the request when it targets GitHub and a token is configured.
        /// </summary>
        /// <param name="request">Request to authenticate.</param>
        /// <returns>True when the request was authenticated.</returns>
        public static bool ApplyAuthentication(HttpRequestMessage request)
        {
            if (request?.RequestUri == null || request.Headers.Authorization != null)
            {
                return false;
            }

            if (!IsGithubUrl(request.RequestUri.AbsoluteUri))
            {
                return false;
            }

            string token = GetToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                Logger.DebugFormat("ApplyAuthentication(): No {0} configured, calling {1} anonymously.", GithubTokenEnvironmentVariable, request.RequestUri.Host);
                return false;
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Logger.DebugFormat("ApplyAuthentication(): Authenticated GitHub request to {0}.", request.RequestUri.Host);
            return true;
        }

        /// <summary>
        /// Sends the request, authenticating it beforehand when it targets GitHub.
        /// </summary>
        /// <param name="httpClient">Client used to send the request.</param>
        /// <param name="request">Request to send.</param>
        /// <returns>The HTTP response.</returns>
        public static System.Threading.Tasks.Task<HttpResponseMessage> SendAuthenticatedAsync(HttpClient httpClient, HttpRequestMessage request)
        {
            ApplyAuthentication(request);
            return httpClient.SendAsync(request);
        }
    }
}
