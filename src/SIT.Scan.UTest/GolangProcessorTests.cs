// --------------------------------------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: 2025 Siemens AG
//
//  SPDX-License-Identifier: MIT
// -------------------------------------------------------------------------------------------------------------------- 

using CycloneDX.Models;
using Moq;
using NUnit.Framework;
using SIT.APICommunications.Model.AQL;
using SIT.Common;
using SIT.Common.Constants;
using SIT.Common.Interface;
using SIT.Common.Model;
using SIT.Scan.Interface;
using SIT.Scan.Model;
using SIT.Services.Interface;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SIT.Scan.UTest
{
    [TestFixture]
    public class GolangProcessorTests
    {
        private GolangProcessor _golangProcessor;
        private Mock<IJFrogService> _mockJFrogService;
        private Mock<IBomHelper> _mockBomHelper;
        private Mock<ICycloneDXBomParser> _mockCycloneDxBomParser;
        private Mock<ISpdxBomParser> _mockSpdxBomParser;

        [SetUp]
        public void Setup()
        {
            _mockJFrogService = new Mock<IJFrogService>();
            _mockBomHelper = new Mock<IBomHelper>();
            _mockCycloneDxBomParser = new Mock<ICycloneDXBomParser>();
            _mockSpdxBomParser = new Mock<ISpdxBomParser>();
            _golangProcessor = new GolangProcessor(_mockCycloneDxBomParser.Object, _mockSpdxBomParser.Object);
        }

        private static CommonAppSettings CreateTestAppSettings()
        {
            return new CommonAppSettings
            {
                ProjectType = "GOLANG",
                Golang = new Config
                {
                    Include = new[] { "*.cdx.json" },
                    Exclude = new string[] { },
                    Artifactory = new Artifactory
                    {
                        InternalRepos = new[] { "golang-internal" },
                        ThirdPartyRepos = new List<ThirdPartyRepo> { new ThirdPartyRepo { Name = "golang-remote", Upload = true } },
                        DevRepos = new[] { "golang-dev" },
                        RemoteRepos = new[] { "golang-remote" }
                    },
                    ReleaseRepo = "golang-release",
                    DevDepRepo = "golang-devdep"
                }
            };
        }

        [Test]
        public void NormalizeGolangComponent_LowercasesPurlAndUsesModulePathAsName()
        {
            var component = new Component
            {
                Group = "github.com/AzureAD",
                Name = "microsoft-authentication-library-for-go",
                Version = "v1.4.2",
                Purl = "pkg:golang/github.com/AzureAD/microsoft-authentication-library-for-go@v1.4.2?goos=linux"
            };

            GolangProcessor.NormalizeGolangComponent(component);

            Assert.AreEqual("github.com/AzureAD/microsoft-authentication-library-for-go", component.Name);
            Assert.AreEqual("pkg:golang/github.com/azuread/microsoft-authentication-library-for-go@v1.4.2", component.Purl);
            Assert.AreEqual(component.Purl, component.BomRef);
        }

        [Test]
        public void NormalizeGolangComponent_ScannerShape_RebuildsModulePathFromPurl()
        {
            // Docker Scout leaves group empty and puts only the last path segment into the name.
            var component = new Component
            {
                Group = null,
                Name = "v2",
                Version = "2.3.0",
                Purl = "pkg:golang/github.com/cespare/xxhash/v2@2.3.0"
            };

            GolangProcessor.NormalizeGolangComponent(component);

            Assert.AreEqual("github.com/cespare/xxhash/v2", component.Name);
            Assert.AreEqual("v2.3.0", component.Version);
            Assert.AreEqual("pkg:golang/github.com/cespare/xxhash/v2@v2.3.0", component.Purl);
        }

        [Test]
        public void NormalizeGolangComponent_ScannerVersionWithoutPrefix_RestoresVPrefix()
        {
            var component = new Component
            {
                Name = "goleak",
                Version = "1.3.0",
                Purl = "pkg:golang/go.uber.org/goleak@1.3.0"
            };

            GolangProcessor.NormalizeGolangComponent(component);

            Assert.AreEqual("go.uber.org/goleak", component.Name);
            Assert.AreEqual("v1.3.0", component.Version);
            Assert.AreEqual("pkg:golang/go.uber.org/goleak@v1.3.0", component.Purl);
        }

        [Test]
        public void NormalizeGolangComponent_KeepsMajorVersionSuffixInModulePath()
        {
            var component = new Component
            {
                Group = "github.com/go-viper",
                Name = "mapstructure/v2",
                Version = "v2.2.1",
                Purl = "pkg:golang/github.com/go-viper/mapstructure/v2@v2.2.1?goarch=arm64&goos=linux&type=module"
            };

            GolangProcessor.NormalizeGolangComponent(component);

            Assert.AreEqual("github.com/go-viper/mapstructure/v2", component.Name);
            Assert.AreEqual("pkg:golang/github.com/go-viper/mapstructure/v2@v2.2.1", component.Purl);
        }

        [Test]
        public void NormalizeGolangComponent_KeepsIncompatibleSuffixAndDecodesVersion()
        {
            // SW360 stores the release version decoded while the purl keeps it percent encoded.
            var component = new Component
            {
                Name = "docker",
                Version = "28.3.3+incompatible",
                Purl = "pkg:golang/github.com/docker/docker@v28.3.3%2Bincompatible"
            };

            GolangProcessor.NormalizeGolangComponent(component);

            Assert.AreEqual("github.com/docker/docker", component.Name);
            Assert.AreEqual("v28.3.3+incompatible", component.Version);
            Assert.AreEqual("pkg:golang/github.com/docker/docker@v28.3.3%2Bincompatible", component.Purl);
        }

        [Test]
        public void NormalizeGolangComponent_KeepsPseudoVersionUnchanged()
        {
            var component = new Component
            {
                Group = "golang.org/x",
                Name = "exp",
                Version = "v0.0.0-20250106191152-7588d65b2ba8",
                Purl = "pkg:golang/golang.org/x/exp@v0.0.0-20250106191152-7588d65b2ba8"
            };

            GolangProcessor.NormalizeGolangComponent(component);

            Assert.AreEqual("golang.org/x/exp", component.Name);
            Assert.AreEqual("pkg:golang/golang.org/x/exp@v0.0.0-20250106191152-7588d65b2ba8", component.Purl);
        }
        [Test]
        public void NormalizeGolangComponent_MapsStdlibToGolangComponent()
        {
            var component = new Component
            {
                Name = "stdlib",
                Version = "1.26.5",
                Purl = "pkg:golang/stdlib@1.26.5?type=module"
            };

            GolangProcessor.NormalizeGolangComponent(component);

            Assert.AreEqual("golang", component.Name);
            Assert.AreEqual("1.26.5", component.Version);
            Assert.AreEqual("pkg:golang/stdlib@1.26.5", component.Purl);
        }

        [TestCase("stdlib", true)]
        [TestCase("std", true)]
        [TestCase("golang", true)]
        [TestCase("github.com/pkg/errors", false)]
        public void IsStdlib_ClassifiesModuleCorrectly(string moduleName, bool expected)
        {
            Assert.AreEqual(expected, GolangProcessor.IsStdlib(moduleName));
        }

        [Test]
        public void NormalizeGolangComponent_DoesNotThrowForMissingPurl()
        {
            var component = new Component { Name = "no-purl", Version = "v1.0.0" };

            Assert.DoesNotThrow(() => GolangProcessor.NormalizeGolangComponent(component));
        }

        [Test]
        public async Task IdentificationOfInternalComponents_IdentifiesComponentFromInternalRepo()
        {
            var appSettings = CreateTestAppSettings();
            var componentData = new ComponentIdentification
            {
                comparisonBOMData = new List<Component>
                {
                    new Component { Name = "github.com/siemens/internal", Version = "v1.0.0", Purl = "pkg:golang/github.com/siemens/internal@v1.0.0" }
                }
            };

            _mockBomHelper.Setup(b => b.GetGolangListOfComponentsFromRepo(It.IsAny<string[]>(), _mockJFrogService.Object))
                .ReturnsAsync(new List<AqlResult>
                {
                    new AqlResult
                    {
                        Repo = "golang-internal",
                        Properties = new List<AqlProperty>
                        {
                            new AqlProperty { Key = "go.name", Value = "github.com/siemens/internal" },
                            new AqlProperty { Key = "go.version", Value = "v1.0.0" }
                        }
                    }
                });

            var result = await _golangProcessor.IdentificationOfInternalComponents(componentData, appSettings, _mockJFrogService.Object, _mockBomHelper.Object);

            Assert.AreEqual(1, result.internalComponents.Count);
        }

        [Test]
        public async Task IdentificationOfInternalComponents_HandlesNoInternalComponents()
        {
            var appSettings = CreateTestAppSettings();
            var componentData = new ComponentIdentification
            {
                comparisonBOMData = new List<Component>
                {
                    new Component { Name = "github.com/pkg/errors", Version = "v0.9.1", Purl = "pkg:golang/github.com/pkg/errors@v0.9.1" }
                }
            };

            _mockBomHelper.Setup(b => b.GetGolangListOfComponentsFromRepo(It.IsAny<string[]>(), _mockJFrogService.Object))
                .ReturnsAsync(new List<AqlResult>());

            var result = await _golangProcessor.IdentificationOfInternalComponents(componentData, appSettings, _mockJFrogService.Object, _mockBomHelper.Object);

            Assert.IsEmpty(result.internalComponents ?? new List<Component>());
        }

        [Test]
        public async Task GetJfrogRepoDetailsOfAComponent_SetsProjectTypeProperty()
        {
            var appSettings = CreateTestAppSettings();
            var components = new List<Component>
            {
                new Component { Name = "github.com/pkg/errors", Version = "v0.9.1", Purl = "pkg:golang/github.com/pkg/errors@v0.9.1" }
            };

            _mockBomHelper.Setup(b => b.GetGolangListOfComponentsFromRepo(It.IsAny<string[]>(), _mockJFrogService.Object))
                .ReturnsAsync(new List<AqlResult>());

            var result = await _golangProcessor.GetJfrogRepoDetailsOfAComponent(components, appSettings, _mockJFrogService.Object, _mockBomHelper.Object);

            Assert.AreEqual(1, result.Count);
            var projectType = result[0].Properties.FirstOrDefault(p => p.Name == Dataconstant.Cdx_ProjectType);
            Assert.IsNotNull(projectType);
            Assert.AreEqual("GOLANG", projectType.Value);
        }

        [Test]
        public async Task GetJfrogRepoDetailsOfAComponent_ResolvesRepoNameFromAqlResult()
        {
            var appSettings = CreateTestAppSettings();
            var components = new List<Component>
            {
                new Component { Name = "github.com/pkg/errors", Version = "v0.9.1", Purl = "pkg:golang/github.com/pkg/errors@v0.9.1" }
            };

            _mockBomHelper.Setup(b => b.GetGolangListOfComponentsFromRepo(It.IsAny<string[]>(), _mockJFrogService.Object))
                .ReturnsAsync(new List<AqlResult>
                {
                    new AqlResult
                    {
                        Repo = "golang-remote",
                        Path = "github.com/pkg/errors/@v",
                        Name = "v0.9.1.zip",
                        Properties = new List<AqlProperty>
                        {
                            new AqlProperty { Key = "go.name", Value = "github.com/pkg/errors" },
                            new AqlProperty { Key = "go.version", Value = "v0.9.1" }
                        }
                    }
                });

            var result = await _golangProcessor.GetJfrogRepoDetailsOfAComponent(components, appSettings, _mockJFrogService.Object, _mockBomHelper.Object);

            var repoName = result[0].Properties.FirstOrDefault(p => p.Name == Dataconstant.Cdx_ArtifactoryRepoName);
            Assert.IsNotNull(repoName);
            Assert.AreEqual("golang-remote", repoName.Value);
        }
    }
}
