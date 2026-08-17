// --------------------------------------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: 2025 Siemens AG
//
//  SPDX-License-Identifier: MIT
// -------------------------------------------------------------------------------------------------------------------- 

using CycloneDX.Models;
using log4net;
using SIT.APICommunications.Model.AQL;
using SIT.Common;
using SIT.Common.Constants;
using SIT.Common.Interface;
using SIT.Common.Logging;
using SIT.Scan.Interface;
using SIT.Scan.Model;
using SIT.Services.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dependency = CycloneDX.Models.Dependency;

namespace SIT.Scan
{
    /// <summary>
    /// The Golang (Go modules) processor. Consumes CycloneDX and SPDX SBOMs produced by image scanners.
    /// </summary>
    public class GolangProcessor(ICycloneDXBomParser cycloneDXBomParser, ISpdxBomParser spdxBomParser) : CycloneDXBomParser, IParser
    {
        #region Fields
        private const string ProjectTypeGolang = "GOLANG";
        private const string NotFoundInRepo = "Not Found in JFrogRepo";
        private const string StdlibModule = "stdlib";
        private const string StdlibComponentName = "golang";
        static readonly ILog Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly ICycloneDXBomParser _cycloneDXBomParser = cycloneDXBomParser;
        private readonly ISpdxBomParser _spdxBomParser = spdxBomParser;
        private static Bom ListUnsupportedComponentsForBom = new Bom { Components = new List<Component>(), Dependencies = new List<Dependency>() };
        private readonly IEnvironmentHelper environmentHelper = new EnvironmentHelper();
        private List<Component> listOfInternalComponents = new List<Component>();
        #endregion

        #region public methods
        /// <summary>
        /// Parses the input SBOM files and builds a BOM of Go modules.
        /// </summary>
        /// <param name="appSettings">Application settings containing input folder and configuration.</param>
        /// <param name="unSupportedBomList">Reference BOM to be filled with unsupported components.</param>
        /// <returns>Constructed CycloneDX Bom.</returns>
        public Bom ParsePackageFile(CommonAppSettings appSettings, ref Bom unSupportedBomList)
        {
            Logger.Debug("ParsePackageFile():Started Parsing input File for Golang components.");
            Bom bom = new Bom();
            ParsingInputFileForBOM(appSettings, ref bom);

            List<Component> componentsForBOM = bom.Components;
            componentsForBOM = BomHelper.GetExcludedComponentsList(componentsForBOM, Dataconstant.PurlCheck()[ProjectTypeGolang], appSettings?.ProjectType);
            componentsForBOM = componentsForBOM.Distinct(new ComponentEqualityComparer()).ToList();

            bom.Components = componentsForBOM;
            bom.Dependencies = CommonHelper.RemoveInvalidDependenciesAndReferences(bom.Components, bom.Dependencies);

            int totalUnsupportedComponentsIdentified = ListUnsupportedComponentsForBom.Components.Count;
            BomCreator.bomKpiData.ComponentsinPackageLockJsonFile += ListUnsupportedComponentsForBom.Components.Count;
            ListUnsupportedComponentsForBom.Components = ListUnsupportedComponentsForBom.Components.Distinct(new ComponentEqualityComparer()).ToList();
            BomCreator.bomKpiData.DuplicateComponents += totalUnsupportedComponentsIdentified - ListUnsupportedComponentsForBom.Components.Count;
            ListUnsupportedComponentsForBom.Dependencies = CommonHelper.RemoveInvalidDependenciesAndReferences(ListUnsupportedComponentsForBom.Components, ListUnsupportedComponentsForBom.Dependencies);
            CommonHelper.AddSiemensDirectProperty(ref ListUnsupportedComponentsForBom);
            unSupportedBomList.Components = ListUnsupportedComponentsForBom.Components;
            unSupportedBomList.Dependencies = ListUnsupportedComponentsForBom.Dependencies;

            Logger.Debug("ParsePackageFile():Completed Parsing input File for Golang components.\n");
            return bom;
        }

        /// <summary>
        /// Asynchronously identifies which components are internal by comparing against JFrog AQL results.
        /// </summary>
        /// <param name="componentData">Component identification data that contains components to check.</param>
        /// <param name="appSettings">Application settings containing repository configuration.</param>
        /// <param name="jFrogService">JFrog service to query for components.</param>
        /// <param name="bomhelper">BOM helper with repository query helpers.</param>
        /// <returns>Asynchronously returns the updated component identification data.</returns>
        public async Task<ComponentIdentification> IdentificationOfInternalComponents(ComponentIdentification componentData, CommonAppSettings appSettings,
            IJFrogService jFrogService, IBomHelper bomhelper)
        {
            Logger.Debug("IdentificationOfInternalComponents(): Starting identification of internal components.");
            List<AqlResult> aqlResultList =
                await bomhelper.GetGolangListOfComponentsFromRepo(appSettings.Golang?.Artifactory?.InternalRepos, jFrogService);
            var (processedComponents, internalComponents) = CommonHelper.ProcessInternalComponentIdentification(
                componentData.comparisonBOMData,
                component => IsInternalGolangComponent(aqlResultList, component));
            componentData.comparisonBOMData = processedComponents;
            componentData.internalComponents = internalComponents;
            listOfInternalComponents = internalComponents;
            Logger.DebugFormat("IdentificationOfInternalComponents(): identified internal components:{0}.", internalComponents.Count);
            Logger.Debug("IdentificationOfInternalComponents(): Completed identification of internal components\n");
            return componentData;
        }

        /// <summary>
        /// Asynchronously enriches components with JFrog repository details (repo name, filename, path, hashes).
        /// </summary>
        /// <param name="componentsForBOM">List of components to enrich.</param>
        /// <param name="appSettings">Application settings that may contain repository lists.</param>
        /// <param name="jFrogService">JFrog service for queries.</param>
        /// <param name="bomhelper">BOM helper utilities.</param>
        /// <returns>Asynchronously returns a modified list of components with JFrog details.</returns>
        public async Task<List<Component>> GetJfrogRepoDetailsOfAComponent(List<Component> componentsForBOM, CommonAppSettings appSettings, IJFrogService jFrogService, IBomHelper bomhelper)
        {
            Logger.Debug("GetJfrogRepoDetailsOfAComponent():Starting to retrieve JFrog repository details for components.\n");
            string[] repoList = CommonHelper.GetRepoList(appSettings);
            List<AqlResult> aqlResultList = await bomhelper.GetGolangListOfComponentsFromRepo(repoList, jFrogService);
            Property projectType = new() { Name = Dataconstant.Cdx_ProjectType, Value = appSettings.ProjectType };
            List<Component> modifiedBOM = new List<Component>();

            foreach (var component in componentsForBOM)
            {
                modifiedBOM.Add(ProcessGolangComponent(component, aqlResultList, appSettings, projectType));
            }
            LogHandlingHelper.IdentifierComponentsData(componentsForBOM, listOfInternalComponents);
            Logger.Debug("GetJfrogRepoDetailsOfAComponent():Completed retrieving JFrog repository details for components.\n");
            return modifiedBOM;
        }
        #endregion

        #region normalization
        /// <summary>
        /// Aligns a Go component with the SW360 conventions: the name carries the full module path, the
        /// version keeps the 'v' prefix and the purl name is lower cased as the package-url spec requires.
        /// </summary>
        /// <param name="component">Component to normalize in place.</param>
        public static void NormalizeGolangComponent(Component component)
        {
            if (component == null || string.IsNullOrEmpty(component.Purl))
            {
                return;
            }

            string modulePath = GetModulePath(component);
            string version = GetVersion(component);

            if (IsStdlib(modulePath))
            {
                component.Name = StdlibComponentName;
                component.Version = version?.TrimStart('v');
            }
            else
            {
                component.Name = modulePath;
                component.Version = EnsureVersionPrefix(version);
            }

            component.Group = string.Empty;
            component.Purl = BuildPurl(modulePath, component.Version);
            component.BomRef = component.Purl;
        }

        /// <summary>
        /// Determines whether the module denotes the Go standard library rather than a third party module.
        /// </summary>
        /// <param name="moduleName">Module path or component name.</param>
        /// <returns>True when the component represents the Go standard library.</returns>
        public static bool IsStdlib(string moduleName)
        {
            return string.Equals(moduleName, StdlibModule, StringComparison.OrdinalIgnoreCase)
                || string.Equals(moduleName, "std", StringComparison.OrdinalIgnoreCase)
                || string.Equals(moduleName, StdlibComponentName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the full module path. Image scanners only put the last path segment into the component name,
        /// so the purl is the authoritative source.
        /// </summary>
        private static string GetModulePath(Component component)
        {
            string purlModule = GetPurlSegment(component.Purl, out _);
            if (!string.IsNullOrEmpty(purlModule))
            {
                return purlModule;
            }

            if (!string.IsNullOrEmpty(component.Group) && !component.Name.StartsWith(component.Group, StringComparison.Ordinal))
            {
                return $"{component.Group}{Dataconstant.ForwardSlash}{component.Name}";
            }
            return component.Name;
        }

        /// <summary>
        /// Prefers the version encoded in the purl because scanners report it inconsistently on the component.
        /// </summary>
        private static string GetVersion(Component component)
        {
            GetPurlSegment(component.Purl, out string purlVersion);
            string version = string.IsNullOrEmpty(purlVersion) ? component.Version : purlVersion;
            // A purl carries "+incompatible" percent encoded, SW360 stores the release version decoded.
            return string.IsNullOrEmpty(version) ? version : version.Replace("%2B", "+", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Splits a Go purl into its module path and version. Image scanners put the tail of the module
        /// path into the purl subpath, so it is appended rather than dropped.
        /// </summary>
        private static string GetPurlSegment(string purl, out string version)
        {
            version = string.Empty;
            string prefix = $"{Dataconstant.PurlCheck()[ProjectTypeGolang]}{Dataconstant.ForwardSlash}";
            if (string.IsNullOrEmpty(purl) || !purl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string remainder = purl[prefix.Length..];

            string subpath = string.Empty;
            int subpathSeparator = remainder.IndexOf('#');
            if (subpathSeparator >= 0)
            {
                subpath = remainder[(subpathSeparator + 1)..];
                remainder = remainder[..subpathSeparator];
            }

            int qualifierSeparator = remainder.IndexOf('?');
            if (qualifierSeparator >= 0)
            {
                remainder = remainder[..qualifierSeparator];
            }

            string module = remainder;
            int versionSeparator = remainder.IndexOf('@');
            if (versionSeparator >= 0)
            {
                version = remainder[(versionSeparator + 1)..];
                module = remainder[..versionSeparator];
            }

            if (!string.IsNullOrEmpty(subpath))
            {
                module = $"{module}{Dataconstant.ForwardSlash}{subpath.Trim('/')}";
            }

            return module;
        }

        /// <summary>
        /// Restores the 'v' prefix that image scanners strip from Go module versions.
        /// </summary>
        private static string EnsureVersionPrefix(string version)
        {
            if (string.IsNullOrEmpty(version) || version.StartsWith('v'))
            {
                return version;
            }
            return char.IsDigit(version[0]) ? $"v{version}" : version;
        }

        /// <summary>
        /// Builds the canonical release purl. Qualifiers and subpaths are intentionally omitted.
        /// </summary>
        private static string BuildPurl(string moduleName, string version)
        {
            string purlName = IsStdlib(moduleName) ? StdlibModule : moduleName;
            string purlVersion = version?.Replace("+", "%2B", StringComparison.Ordinal);
            return $"{Dataconstant.PurlCheck()[ProjectTypeGolang]}{Dataconstant.ForwardSlash}{purlName.ToLowerInvariant()}@{purlVersion}";
        }
        #endregion

        #region private methods
        /// <summary>
        /// Determines whether the component was found in one of the configured internal repositories.
        /// </summary>
        private static bool IsInternalGolangComponent(List<AqlResult> aqlResultList, Component component)
        {
            return aqlResultList.Exists(x => MatchesGolangComponent(x, component.Name, component.Version));
        }

        /// <summary>
        /// Matches an AQL entry against a module, tolerating repositories that do not publish go properties.
        /// </summary>
        private static bool MatchesGolangComponent(AqlResult aqlResult, string name, string version)
        {
            bool propertyMatch = aqlResult.Properties != null
                && aqlResult.Properties.Any(p => p.Key == "go.name" && string.Equals(p.Value, name, StringComparison.OrdinalIgnoreCase))
                && aqlResult.Properties.Any(p => p.Key == "go.version" && string.Equals(p.Value, version, StringComparison.OrdinalIgnoreCase));
            if (propertyMatch)
            {
                return true;
            }

            // JFrog stores Go modules as <module path>/@v/<version>.zip
            string expectedPath = $"{name}/@v";
            return !string.IsNullOrEmpty(aqlResult.Path)
                && aqlResult.Path.EndsWith(expectedPath, StringComparison.OrdinalIgnoreCase)
                && aqlResult.Name.StartsWith(version, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Processes a single Go component, selects the artifactory repo and sets properties/hashes.
        /// </summary>
        private static Component ProcessGolangComponent(Component component, List<AqlResult> aqlResultList, CommonAppSettings appSettings, Property projectType)
        {
            Logger.DebugFormat("GetArtifactoryRepoName(): Starting identify JFrog repository details retrieval for component [Name: {0}, Version: {1}].", component.Name, component.Version);
            string repoName = GetArtifactoryRepoName(aqlResultList, component, out string jfrogPackageName, out string jfrogRepoPath);
            var hashes = aqlResultList.FirstOrDefault(x => MatchesGolangComponent(x, component.Name, component.Version));

            Property artifactoryrepo = new() { Name = Dataconstant.Cdx_ArtifactoryRepoName, Value = repoName };
            Property fileNameProperty = new() { Name = Dataconstant.Cdx_Siemensfilename, Value = jfrogPackageName };
            Property jfrogRepoPathProperty = new() { Name = Dataconstant.Cdx_JfrogRepoPath, Value = jfrogRepoPath };

            UpdateGolangKpiDataBasedOnRepo(repoName, appSettings);
            CommonHelper.SetComponentPropertiesAndHashes(component, artifactoryrepo, projectType, fileNameProperty, jfrogRepoPathProperty, hashes);

            return component;
        }

        /// <summary>
        /// Updates KPI counters based on which repository a component was found in.
        /// </summary>
        private static void UpdateGolangKpiDataBasedOnRepo(string repoValue, CommonAppSettings appSettings)
        {
            if (repoValue == appSettings.Golang?.DevDepRepo)
            {
                BomCreator.bomKpiData.DevdependencyComponents++;
            }

            if (appSettings.Golang?.Artifactory?.ThirdPartyRepos != null)
            {
                foreach (var thirdPartyRepo in appSettings.Golang.Artifactory.ThirdPartyRepos)
                {
                    if (repoValue == thirdPartyRepo.Name)
                    {
                        BomCreator.bomKpiData.ThirdPartyRepoComponents++;
                        break;
                    }
                }
            }

            if (repoValue == appSettings.Golang?.ReleaseRepo)
            {
                BomCreator.bomKpiData.ReleaseRepoComponents++;
            }

            if (repoValue == Dataconstant.NotFoundInJFrog || repoValue == "")
            {
                BomCreator.bomKpiData.UnofficialComponents++;
            }
        }

        /// <summary>
        /// Determines the artifactory repository name for a component by analyzing AQL results.
        /// </summary>
        private static string GetArtifactoryRepoName(List<AqlResult> aqlResultList, Component component, out string jfrogPackageName, out string jfrogRepoPath)
        {
            jfrogPackageName = Dataconstant.PackageNameNotFoundInJfrog;
            jfrogRepoPath = Dataconstant.JfrogRepoPathNotFound;

            var aqlResults = aqlResultList.FindAll(x => MatchesGolangComponent(x, component.Name, component.Version));
            if (aqlResults.Count > 0)
            {
                jfrogPackageName = aqlResults[0].Name;
            }

            string repoName = CommonIdentiferHelper.GetRepodetailsFromPerticularOrder(aqlResults, component);
            if (!repoName.Equals(NotFoundInRepo, StringComparison.OrdinalIgnoreCase))
            {
                var aqlResult = aqlResults.FirstOrDefault(x => x.Repo.Equals(repoName));
                jfrogRepoPath = GetJfrogRepoPath(aqlResult);
            }

            return repoName;
        }

        /// <summary>
        /// Builds a repository path string from an AQL result entry.
        /// </summary>
        private static string GetJfrogRepoPath(AqlResult aqlResult)
        {
            if (aqlResult == null)
            {
                return Dataconstant.JfrogRepoPathNotFound;
            }

            if (string.IsNullOrEmpty(aqlResult.Path) || aqlResult.Path.Equals("."))
            {
                return $"{aqlResult.Repo}/{aqlResult.Name}";
            }

            return $"{aqlResult.Repo}/{aqlResult.Path}/{aqlResult.Name}";
        }

        /// <summary>
        /// Parses the configured input files to populate the BOM with components and dependencies.
        /// </summary>
        private void ParsingInputFileForBOM(CommonAppSettings appSettings, ref Bom bom)
        {
            var configFiles = FolderScanner.FileScanner(appSettings.Directory.InputFolder, appSettings.Golang, environmentHelper);
            var componentsForBOM = new List<Component>();
            var dependencies = new List<Dependency>();
            var templateBomFilePaths = new List<string>();

            foreach (var filepath in configFiles)
            {
                InputfilesProcess(filepath, appSettings, ref bom, componentsForBOM, dependencies, templateBomFilePaths);
            }

            BomFileDataProcess(ref bom, componentsForBOM, dependencies, templateBomFilePaths, appSettings);
        }

        /// <summary>
        /// Processes a single input file and dispatches to the appropriate parser depending on extension.
        /// </summary>
        private void InputfilesProcess(
            string filepath,
            CommonAppSettings appSettings,
            ref Bom bom,
            List<Component> componentsForBOM,
            List<Dependency> dependencies,
            List<string> templateBomFilePaths)
        {
            if (filepath.EndsWith(FileConstant.SBOMTemplateFileExtension))
            {
                templateBomFilePaths.Add(filepath);
            }
            else if (filepath.EndsWith(FileConstant.SPDXFileExtension))
            {
                ParseSpdxFile(filepath, appSettings, ref bom, componentsForBOM, dependencies);
            }
            else if (filepath.EndsWith(FileConstant.CycloneDXFileExtension))
            {
                ParseCycloneDxFile(filepath, appSettings, ref bom, componentsForBOM, dependencies);
            }
        }

        /// <summary>
        /// Parses an SPDX file, validates signature convention, and extracts components and dependencies.
        /// </summary>
        private void ParseSpdxFile(
            string filepath,
            CommonAppSettings appSettings,
            ref Bom bom,
            List<Component> componentsForBOM,
            List<Dependency> dependencies)
        {
            Logger.DebugFormat("ParsingInputFileForBOM():Spdx file detected: {0}", filepath);
            BomHelper.NamingConventionOfSPDXFile(filepath, appSettings);
            var listUnsupportedComponents = new Bom { Components = new List<Component>(), Dependencies = new List<Dependency>() };
            bom = _spdxBomParser.ParseSPDXBom(filepath);
            SpdxSbomHelper.CheckValidComponentsFromSpdxfile(bom, appSettings.ProjectType, ref listUnsupportedComponents);
            SpdxSbomHelper.AddSpdxSBomFileNameProperty(ref bom, filepath);
            NormalizeComponents(bom.Components, bom.Dependencies);
            LogHandlingHelper.IdentifierInputFileComponents(filepath, bom.Components);
            componentsForBOM.AddRange(bom.Components);
            dependencies.AddRange(bom.Dependencies);
            SpdxSbomHelper.AddSpdxPropertysForUnsupportedComponents(listUnsupportedComponents.Components, filepath);
            ListUnsupportedComponentsForBom.Components.AddRange(listUnsupportedComponents.Components);
            ListUnsupportedComponentsForBom.Dependencies.AddRange(listUnsupportedComponents.Dependencies);
        }

        /// <summary>
        /// Parses a CycloneDX file and extracts components to the BOM.
        /// </summary>
        private void ParseCycloneDxFile(
            string filepath,
            CommonAppSettings appSettings,
            ref Bom bom,
            List<Component> componentsForBOM,
            List<Dependency> dependencies)
        {
            Logger.DebugFormat("ParsingInputFileForBOM():CycloneDX file detected: {0}", filepath);
            bom = _cycloneDXBomParser.ParseCycloneDXBom(filepath);
            CheckValidComponentsForProjectType(bom.Components, appSettings.ProjectType);
            NormalizeComponents(bom.Components, bom.Dependencies);
            BomHelper.GetDetailsforManuallyAddedComp(bom.Components);
            LogHandlingHelper.IdentifierInputFileComponents(filepath, bom.Components);
            if (bom.Components != null)
            {
                CommonHelper.AddSiemensDirectProperty(ref bom);
                componentsForBOM.AddRange(bom.Components);
            }
            if (bom.Dependencies != null)
            {
                dependencies.AddRange(bom.Dependencies);
            }
        }

        /// <summary>
        /// Normalizes every Go component of the parsed BOM and keeps the dependency graph pointing at the
        /// rewritten bom-refs.
        /// </summary>
        private static void NormalizeComponents(List<Component> components, List<Dependency> dependencies)
        {
            var rewrittenRefs = new Dictionary<string, string>();
            foreach (var component in components ?? new List<Component>())
            {
                if (string.IsNullOrEmpty(component.Purl) || !component.Purl.Contains(Dataconstant.PurlCheck()[ProjectTypeGolang], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string previousRef = component.BomRef;
                NormalizeGolangComponent(component);
                if (!string.IsNullOrEmpty(previousRef) && previousRef != component.BomRef)
                {
                    rewrittenRefs[previousRef] = component.BomRef;
                }
            }

            if (rewrittenRefs.Count > 0)
            {
                RemapDependencyRefs(dependencies, rewrittenRefs);
            }
        }

        /// <summary>
        /// Applies the bom-ref rewrites to the dependency tree.
        /// </summary>
        private static void RemapDependencyRefs(List<Dependency> dependencies, Dictionary<string, string> rewrittenRefs)
        {
            foreach (var dependency in dependencies ?? new List<Dependency>())
            {
                if (!string.IsNullOrEmpty(dependency.Ref) && rewrittenRefs.TryGetValue(dependency.Ref, out string updatedRef))
                {
                    dependency.Ref = updatedRef;
                }
                RemapDependencyRefs(dependency.Dependencies, rewrittenRefs);
            }
        }

        /// <summary>
        /// Finalizes BOM file data processing: deduplicates, merges dependencies, applies templates and filters.
        /// </summary>
        private void BomFileDataProcess(
            ref Bom bom,
            List<Component> componentsForBOM,
            List<Dependency> dependencies,
            List<string> templateBomFilePaths,
            CommonAppSettings appSettings)
        {
            int initialCount = componentsForBOM.Count;
            BomCreator.bomKpiData.ComponentsinPackageLockJsonFile = componentsForBOM.Count;
            BomHelper.GetDistinctComponentList(ref componentsForBOM);
            BomCreator.bomKpiData.DuplicateComponents = initialCount - componentsForBOM.Count;

            bom.Components = componentsForBOM;
            bom.Dependencies = dependencies;

            string templateFilePath = SbomTemplate.GetFilePathForTemplate(templateBomFilePaths);
            SbomTemplate.ProcessTemplateFile(templateFilePath, _cycloneDXBomParser, bom.Components, appSettings.ProjectType);

            bom = BomHelper.RemoveExcludedComponents(appSettings, bom);
            bom.Dependencies = bom.Dependencies?.GroupBy(x => new { x.Ref }).Select(y => y.First()).ToList();
        }
        #endregion
    }
}
