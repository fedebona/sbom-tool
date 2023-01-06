﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using AutoMapper;
using AutoMapper.Configuration;
using Microsoft.Sbom.Api.Config;
using Microsoft.Sbom.Api.Config.Extensions;
using Microsoft.Sbom.Api.Manifest;
using Microsoft.Sbom.Api.Output.Telemetry;
using Microsoft.Sbom.Api.Utils;
using Microsoft.Sbom.Api.Workflows;
using Microsoft.Sbom.Common.Config;
using Microsoft.Sbom.Common.Config.Validators;
using Microsoft.Sbom.Contracts;
using Microsoft.Sbom.Contracts.Enums;
using PowerArgs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using IConfiguration = Microsoft.Sbom.Common.Config.IConfiguration;

namespace Microsoft.Sbom.Api
{
    /// <summary>
    /// Responsible for an API to generate SBOMs.
    /// </summary>
    public class SBOMGenerator : ISBOMGenerator
    {
        private readonly IWorkflow<SBOMGenerationWorkflow> generationWorkflow;
        private readonly ManifestGeneratorProvider generatorProvider;
        private readonly IRecorder recorder;
        private readonly IEnumerable<ConfigValidator> configValidators;
        private readonly ConfigSanitizer configSanitizer;

        public SBOMGenerator(
            IWorkflow<SBOMGenerationWorkflow> generationWorkflow, 
            ManifestGeneratorProvider generatorProvider, 
            IRecorder recorder, 
            IEnumerable<ConfigValidator> configValidators, 
            ConfigSanitizer configSanitizer)
        {
            this.generationWorkflow = generationWorkflow;
            this.generatorProvider = generatorProvider;
            this.recorder = recorder;
            this.configValidators = configValidators;
            this.configSanitizer = configSanitizer;
        }

        /// <inheritdoc />
        public async Task<SBOMGenerationResult> GenerateSBOMAsync(
            string rootPath,
            string componentPath,
            SBOMMetadata metadata,
            IList<SBOMSpecification> specifications = null,
            RuntimeConfiguration runtimeConfiguration = null,
            string manifestDirPath = null,
            string externalDocumentReferenceListFile = null)
        {
            // Get scan configuration
            var inputConfiguration = ApiConfigurationBuilder.GetConfiguration(
                rootPath,
                manifestDirPath, null, null, metadata, specifications,
                runtimeConfiguration, externalDocumentReferenceListFile, componentPath);

            // Initialize the IOC container. This varies depending on the configuration.
            inputConfiguration = ValidateConfig(inputConfiguration);

            inputConfiguration.ToConfiguration();

            // This is the generate workflow
            bool isSuccess = await generationWorkflow.RunAsync();

            // TODO: Telemetry?
            await recorder.FinalizeAndLogTelemetryAsync();

            var entityErrors = recorder.Errors.Select(error => error.ToEntityError()).ToList();

            return new SBOMGenerationResult(isSuccess, entityErrors);
        }

        /// <inheritdoc />
        public IEnumerable<AlgorithmName> GetRequiredAlgorithms(SBOMSpecification specification)
        {
            ArgumentNullException.ThrowIfNull(specification);

            // The provider will throw if the generator is not found.
            var generator = generatorProvider.Get(specification.ToManifestInfo());

            return generator
                    .RequiredHashAlgorithms
                    .ToList();
        }

        public IEnumerable<SBOMSpecification> GetSupportedSBOMSpecifications()
        {
            return generatorProvider
                    .GetSupportedManifestInfos()
                    .Select(g => g.ToSBOMSpecification())
                    .ToList();
        }

        private InputConfiguration ValidateConfig(InputConfiguration config)
        {
            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(config))
            {
                configValidators.ForEach(v =>
                {
                    v.CurrentAction = config.ManifestToolAction;
                    v.Validate(property.DisplayName, property.GetValue(config), property.Attributes);
                });
            }

            configSanitizer.SanitizeConfig(config);
            return config;
        }
    }
}
