// --------------------------------------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: 2025 Siemens AG
//
//  SPDX-License-Identifier: MIT
// -------------------------------------------------------------------------------------------------------------------- 

using NUnit.Framework;
using SIT.Common;
using System;
using System.Net.Http;

namespace SIT.Common.UTests
{
    [TestFixture]
    public class GithubAuthHelperTest
    {
        private string _originalToken;

        [SetUp]
        public void Setup()
        {
            _originalToken = Environment.GetEnvironmentVariable(GithubAuthHelper.GithubTokenEnvironmentVariable);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(GithubAuthHelper.GithubTokenEnvironmentVariable, _originalToken);
        }

        [TestCase("https://api.github.com/repos/siemens/continuous-clearing/tags", true)]
        [TestCase("https://github.com/pkg/errors/archive/refs/tags/v0.9.1.zip", true)]
        [TestCase("https://raw.githubusercontent.com/conan-io/conan-center-index/master/recipes/zlib/all/conandata.yml", true)]
        [TestCase("https://proxy.golang.org/github.com/pkg/errors/@v/v0.9.1.zip", false)]
        [TestCase("https://gitlab.com/alpine/aports.git", false)]
        [TestCase("not-a-url", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsGithubUrl_DetectsGithubHosts(string url, bool expected)
        {
            Assert.AreEqual(expected, GithubAuthHelper.IsGithubUrl(url));
        }

        [Test]
        public void ApplyAuthentication_GithubUrlWithToken_AddsBearerHeader()
        {
            Environment.SetEnvironmentVariable(GithubAuthHelper.GithubTokenEnvironmentVariable, "test-token");
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/siemens/continuous-clearing/tags");

            bool applied = GithubAuthHelper.ApplyAuthentication(request);

            Assert.IsTrue(applied);
            Assert.AreEqual("Bearer", request.Headers.Authorization.Scheme);
            Assert.AreEqual("test-token", request.Headers.Authorization.Parameter);
        }

        [Test]
        public void ApplyAuthentication_GithubUrlWithoutToken_LeavesRequestAnonymous()
        {
            Environment.SetEnvironmentVariable(GithubAuthHelper.GithubTokenEnvironmentVariable, null);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/siemens/continuous-clearing/tags");

            bool applied = GithubAuthHelper.ApplyAuthentication(request);

            Assert.IsFalse(applied);
            Assert.IsNull(request.Headers.Authorization);
        }

        [Test]
        public void ApplyAuthentication_NonGithubUrl_DoesNotLeakToken()
        {
            Environment.SetEnvironmentVariable(GithubAuthHelper.GithubTokenEnvironmentVariable, "test-token");
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://proxy.golang.org/golang.org/x/sys/@v/v0.33.0.zip");

            bool applied = GithubAuthHelper.ApplyAuthentication(request);

            Assert.IsFalse(applied);
            Assert.IsNull(request.Headers.Authorization);
        }

        [Test]
        public void ApplyAuthentication_NullRequest_ReturnsFalse()
        {
            Assert.IsFalse(GithubAuthHelper.ApplyAuthentication(null));
        }

        [Test]
        public void GetToken_NotConfigured_ReturnsEmptyString()
        {
            Environment.SetEnvironmentVariable(GithubAuthHelper.GithubTokenEnvironmentVariable, null);

            Assert.AreEqual(string.Empty, GithubAuthHelper.GetToken());
        }
    }
}
