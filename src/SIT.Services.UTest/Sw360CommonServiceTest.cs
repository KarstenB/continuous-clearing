// --------------------------------------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: 2025 Siemens AG
//
//  SPDX-License-Identifier: MIT
// -------------------------------------------------------------------------------------------------------------------- 


using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using SIT.APICommunications.Model;
using SIT.Facade.Interfaces;
using SIT.Services.Interface;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Services.UTest
{
    /// <summary>
    /// The Sw360CommonServiceTest class
    /// </summary>
    [TestFixture]
    internal class Sw360CommonServiceTest
    {
        [Test]
        public async Task GetComponentDataByExternalId_PassComponentInfo_ReturnsComponentStatus()
        {
            // Arrange
            Self self = new();
            Links links = new();
            Sw360Components sw360Components = new();
            var componentList = new List<Sw360Components>();
            ComponentEmbedded componentEmbedded = new();

            self.Href = "http://localhost:8090/resource/api/components/uiweriwfoowefih87398r3ur093u0";
            links.Self = self;
            sw360Components.Name = "Zone.js";
            sw360Components.Links = links;
            sw360Components.ExternalIds = new ExternalIds() { Package_Url = "pkg:npm/zone.js" };
            componentList.Add(sw360Components);
            componentEmbedded.Sw360components = componentList;

            ComponentsModel componentsModel = new ComponentsModel();
            componentsModel.Embedded = componentEmbedded;

            var componentsModelSerialized = JsonConvert.SerializeObject(componentsModel);
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage();
            var content = new StringContent(componentsModelSerialized, Encoding.UTF8, "application/json");
            httpResponseMessage.Content = content;

            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetComponentByExternalId(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(httpResponseMessage);

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetComponentDataByExternalId("zone.js", "pkg:npm/zone.js");

            // Assert
            Assert.That(actual, Is.Not.Null);
        }

        [Test]
        public async Task GetComponentDataByExternalId_PassComponentInfo_ThrowsHttpRequestException()
        {
            // Arrange
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetComponentByExternalId(It.IsAny<string>(), It.IsAny<string>())).Throws<HttpRequestException>();

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetComponentDataByExternalId("zone.js", "pkg:npm/zone.js");

            // Assert
            Assert.That(actual, Is.Not.Null);
        }

        [Test]
        public async Task GetComponentDataByExternalId_PassComponentInfo_ThrowsAggregateException()
        {
            // Arrange
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetComponentByExternalId(It.IsAny<string>(), It.IsAny<string>())).Throws<AggregateException>();

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetComponentDataByExternalId("zone.js", "pkg:npm/zone.js");

            // Assert
            Assert.That(actual, Is.Not.Null);
        }

        [Test]
        public async Task GetComponentDataByExternalId_CargoPurlOnUmbrellaComponent_ReportsComponentAsMissing()
        {
            // Arrange
            var umbrella = CreateSw360Component("clap", "[\"pkg:cargo/clap\",\"pkg:cargo/clap_builder\"]");
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetComponentByExternalId(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => ComponentResponse(umbrella));

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetComponentDataByExternalId("clap_builder", "pkg:cargo/clap_builder");

            // Assert
            Assert.That(actual.isComponentExist, Is.False);
        }

        [Test]
        public async Task GetComponentDataByExternalId_CargoPurlWithNameMatch_PrefersComponentCarryingTheCrateName()
        {
            // Arrange
            var umbrella = CreateSw360Component("clap", "[\"pkg:cargo/clap\",\"pkg:cargo/clap_builder\"]");
            var crate = CreateSw360Component("clap_builder", "pkg:cargo/clap_builder");
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetComponentByExternalId(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => ComponentResponse(umbrella, crate));

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetComponentDataByExternalId("clap_builder", "pkg:cargo/clap_builder");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(actual.isComponentExist, Is.True);
                Assert.That(actual.Sw360components.Name, Is.EqualTo("clap_builder"));
            });
        }

        [Test]
        public async Task GetComponentDataByExternalId_NonCargoPurlOnDifferentlyNamedComponent_KeepsPurlMatch()
        {
            // Arrange
            var umbrella = CreateSw360Component("angular", "[\"pkg:npm/@angular/core\",\"pkg:npm/zone.js\"]");
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetComponentByExternalId(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => ComponentResponse(umbrella));

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetComponentDataByExternalId("zone.js", "pkg:npm/zone.js");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(actual.isComponentExist, Is.True);
                Assert.That(actual.Sw360components.Name, Is.EqualTo("angular"));
            });
        }

        [Test]
        public async Task GetReleaseDataByExternalId_CargoPurlOnUmbrellaRelease_ReportsReleaseAsMissing()
        {
            // Arrange
            var umbrella = CreateSw360Release("clap", "4.6.2", "[\"pkg:cargo/clap@4.6.2\",\"pkg:cargo/clap_builder@4.6.2\"]");
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetReleaseByExternalId(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(() => ReleaseResponse(umbrella));

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetReleaseDataByExternalId("clap_builder", "4.6.2", "pkg:cargo/clap_builder@4.6.2");

            // Assert
            Assert.That(actual.isReleaseExist, Is.False);
        }

        private static Sw360Components CreateSw360Component(string name, string packageUrl)
        {
            return new Sw360Components()
            {
                Name = name,
                Links = new Links() { Self = new Self() { Href = $"http://localhost:8090/resource/api/components/{name}" } },
                ExternalIds = new ExternalIds() { Package_Url = packageUrl },
            };
        }

        private static Sw360Releases CreateSw360Release(string name, string version, string packageUrl)
        {
            return new Sw360Releases()
            {
                Name = name,
                Version = version,
                Links = new Links() { Self = new Self() { Href = $"http://localhost:8090/resource/api/releases/{name}" } },
                ExternalIds = new ExternalIds() { Package_Url = packageUrl },
            };
        }

        private static HttpResponseMessage ComponentResponse(params Sw360Components[] components)
        {
            var model = new ComponentsModel()
            {
                Embedded = new ComponentEmbedded() { Sw360components = new List<Sw360Components>(components) },
            };
            return JsonResponse(model);
        }

        private static HttpResponseMessage ReleaseResponse(params Sw360Releases[] releases)
        {
            var model = new ComponentsRelease()
            {
                Embedded = new ReleaseEmbedded() { Sw360Releases = new List<Sw360Releases>(releases) },
            };
            return JsonResponse(model);
        }

        private static HttpResponseMessage JsonResponse(object model)
        {
            return new HttpResponseMessage()
            {
                Content = new StringContent(JsonConvert.SerializeObject(model), Encoding.UTF8, "application/json"),
            };
        }

        [Test]
        public async Task GetReleaseDataByExternalId_PassReleaseInfo_ReturnsReleaseStatus()
        {
            // Arrange
            Self self = new();
            Links links = new();
            Sw360Releases sw360Releases = new();
            var releaseList = new List<Sw360Releases>();
            ReleaseEmbedded releaseEmbedded = new();

            self.Href = "http://localhost:8090/resource/api/releases/uiweriwfoowefih87398r3ur093u0";
            links.Self = self;
            sw360Releases.Name = "Zone.js";
            sw360Releases.Version = "1.0.0";
            sw360Releases.Links = links;
            sw360Releases.ExternalIds = new ExternalIds() { Package_Url = "pkg:npm/zone.js@1.0.0" };
            releaseList.Add(sw360Releases);
            releaseEmbedded.Sw360Releases = releaseList;

            ComponentsRelease componentsRelease = new ComponentsRelease();
            componentsRelease.Embedded = releaseEmbedded;

            var componentsReleaseSerialized = JsonConvert.SerializeObject(componentsRelease);
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage();
            var content = new StringContent(componentsReleaseSerialized, Encoding.UTF8, "application/json");
            httpResponseMessage.Content = content;

            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetReleaseByExternalId(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(httpResponseMessage);

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetReleaseDataByExternalId("zone.js", "1.0.0", "pkg:npm/zone.js@1.0.0");

            // Assert
            Assert.That(actual, Is.Not.Null);
        }

        [Test]
        public async Task GetReleaseDataByExternalId_PassReleaseInfo_ThrowsHttpRequestException()
        {
            // Arrange

            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetReleaseByExternalId(It.IsAny<string>(), It.IsAny<string>())).Throws<HttpRequestException>();

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetReleaseDataByExternalId("zone.js", "1.0.0", "pkg:npm/zone.js@1.0.0");

            // Assert
            Assert.That(actual, Is.Not.Null);
        }

        [Test]
        public async Task GetReleaseDataByExternalId_PassReleaseInfo_ThrowsHttpAggregateException()
        {
            // Arrange

            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetReleaseByExternalId(It.IsAny<string>(), It.IsAny<string>())).Throws<AggregateException>();

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetReleaseDataByExternalId("zone.js", "1.0.0", "pkg:npm/zone.js@1.0.0");

            // Assert
            Assert.That(actual, Is.Not.Null);
        }

        [Test]
        public async Task GetReleaseIdByComponentId_PassComponentId_ReturnsReleaseId()
        {
            ReleaseIdOfComponent releaseIdOfComponent = GetReleaseIdOfCompnentObject();
            var releaseIdOfComponentSerialized = JsonConvert.SerializeObject(releaseIdOfComponent);
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetReleaseOfComponentById(It.IsAny<string>())).ReturnsAsync(releaseIdOfComponentSerialized);

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetReleaseIdByComponentId("uiweriwfoowefih87398r3ur837489", "1.0.0");

            // Assert
            Assert.That("uiweriwfoowefih87398r3ur093u0", Is.EqualTo(actual));
        }

        [Test]
        public async Task GetReleaseIdByComponentId_PassComponentId_ThrowsHttpRequestException()
        {
            // Arrange
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetReleaseOfComponentById(It.IsAny<string>())).Throws<HttpRequestException>();

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetReleaseIdByComponentId("uiweriwfoowefih87398r3ur837489", "1.0.0");

            // Assert
            Assert.That("uiweriwfoowefih87398r3ur093u0", Is.Not.EqualTo(actual));
        }

        [Test]
        public async Task GetReleaseIdByComponentId_PassComponentId_ThrowsAggregateException()
        {
            // Arrange
            Mock<ISW360ApicommunicationFacade> sw360ApiCommunicationFacade = new();
            sw360ApiCommunicationFacade.Setup(
                x => x.GetReleaseOfComponentById(It.IsAny<string>())).Throws<AggregateException>();

            // Act
            ISW360CommonService sW360CommonService = new SW360CommonService(sw360ApiCommunicationFacade.Object);
            var actual = await sW360CommonService.GetReleaseIdByComponentId("uiweriwfoowefih87398r3ur837489", "1.0.0");

            // Assert
            Assert.That("uiweriwfoowefih87398r3ur093u0", Is.Not.EqualTo(actual));
        }

        private static ReleaseIdOfComponent GetReleaseIdOfCompnentObject()
        {
            // Arrange
            ReleaseIdOfComponent releaseIdOfComponent = new();

            Self self = new();
            Links links = new();
            Sw360Releases sw360Releases = new();
            var releaseList = new List<Sw360Releases>();
            ReleaseEmbedded releaseEmbedded = new();

            self.Href = "http://localhost:8090/resource/api/releases/uiweriwfoowefih87398r3ur093u0";
            links.Self = self;
            sw360Releases.Name = "Zone.js";
            sw360Releases.Version = "1.0.0";
            sw360Releases.Links = links;
            releaseList.Add(sw360Releases);
            releaseEmbedded.Sw360Releases = releaseList;
            releaseIdOfComponent.Embedded = releaseEmbedded;
            return releaseIdOfComponent;
        }
        [Test]
        public void GetReleaseExistStatus_ReleaseCollectionContainsMatchingKey_DoesNothing()
        {
            // Arrange
            string name = "Zone.js";
            string externalIdKey = "?package-url=";
            var release = new Sw360Releases
            {
                Name = "Zone.js",
                ExternalIds = new ExternalIds { Package_Url = "[\"pkg:npm/zone.js\"]" }
            };
            var releaseCollection = new Dictionary<int, Sw360Releases>
        {
            { 1, release }
        };
            var sw360ReleasesData = new List<Sw360Releases> { release };

            // Act
            var result = InvokeGetReleaseExistStatus(name, externalIdKey, sw360ReleasesData);

            // Assert
            Assert.IsTrue(result.isReleaseExist);
            Assert.AreEqual(release, result.sw360Releases);
        }

        [Test]
        public void GetReleaseExistStatus_ReleaseCollectionContainsNonMatchingKey_UpdatesCollection()
        {
            // Arrange
            string name = "Zone.js";
            string externalIdKey = "?package-url=";
            var existingRelease = new Sw360Releases
            {
                Name = "OtherRelease",
                ExternalIds = new ExternalIds { Package_Url = "[\"pkg:npm/other\"]" }
            };
            var newRelease = new Sw360Releases
            {
                Name = "Zone.js",
                ExternalIds = new ExternalIds { Package_Url = "[\"pkg:npm/zone.js\"]" }
            };
            var releaseCollection = new Dictionary<int, Sw360Releases>
        {
            { 1, existingRelease }
        };
            var sw360ReleasesData = new List<Sw360Releases> { newRelease };

            // Act
            var result = InvokeGetReleaseExistStatus(name, externalIdKey, sw360ReleasesData);

            // Assert
            Assert.IsTrue(result.isReleaseExist);
            Assert.AreEqual(newRelease, result.sw360Releases);
        }

        [Test]
        public void GetReleaseExistStatus_ReleaseCollectionDoesNotContainKey_AddsToCollection()
        {
            // Arrange
            string name = "Zone.js";
            string externalIdKey = "?package-url=";
            var release = new Sw360Releases
            {
                Name = "Zone.js",
                ExternalIds = new ExternalIds { Package_Url = "[\"pkg:npm/zone.js\"]" }
            };
            var sw360ReleasesData = new List<Sw360Releases> { release };

            // Act
            var result = InvokeGetReleaseExistStatus(name, externalIdKey, sw360ReleasesData);

            // Assert
            Assert.IsTrue(result.isReleaseExist);
            Assert.AreEqual(release, result.sw360Releases);
        }

        private Releasestatus InvokeGetReleaseExistStatus(string name, string externalIdKey, IList<Sw360Releases> sw360ReleasesData)
        {
            // Use reflection to invoke the private method
            var method = typeof(SW360CommonService).GetMethod("GetReleaseExistStatus", BindingFlags.NonPublic | BindingFlags.Static);
            return (Releasestatus)method.Invoke(null, new object[] { name, externalIdKey, sw360ReleasesData });
        }
    }
}
