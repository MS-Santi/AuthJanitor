// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using AuthJanitor.Integrations.CryptographicImplementations;
using Microsoft.Azure.Management.AppService.Fluent;
using Microsoft.Azure.Management.AppService.Fluent.WebAppBase.Update;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AuthJanitor.Providers.AppServices.Functions
{
    /// <summary>
    /// Defines a Functions application which receives key information through an AppConfig setting
    /// </summary>
    [Provider(Name = "Functions App - AppSettings",
              IconClass = "fa fa-bolt",
              Description = "Manages the lifecycle of an Azure Functions app which reads a Managed Secret from its Application Settings")]
    [ProviderImage(ProviderImages.FUNCTIONS_SVG)]
    public class AppSettingsFunctionsApplicationLifecycleProvider : FunctionsApplicationLifecycleProvider<AppSettingConfiguration>
    {
        private readonly ILogger _logger;

        public AppSettingsFunctionsApplicationLifecycleProvider(ILogger<AppSettingsFunctionsApplicationLifecycleProvider> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Call to prepare the application for a new secret, passing in a secret
        /// which will be valid while the Rekeying is taking place (for zero-downtime)
        /// </summary>
        public override async Task BeforeRekeying(List<RegeneratedSecret> temporaryUseSecrets)
        {
            await ApplySecrets(TemporarySlotName, temporaryUseSecrets);
            _logger.LogInformation("BeforeRekeying completed!");
        }

        /// <summary>
        /// Call to commit the newly generated secret
        /// </summary>
        public override async Task CommitNewSecrets(List<RegeneratedSecret> newSecrets)
        {
            await ApplySecrets(TemporarySlotName, newSecrets);
            _logger.LogInformation("CommitNewSecrets completed!");
        }

        /// <summary>
        /// Call after all new keys have been committed
        /// </summary>
        public override async Task AfterRekeying()
        {
            _logger.LogInformation("Swapping to '{SlotName}'", TemporarySlotName);
            await (await GetFunctionsApp()).SwapAsync(TemporarySlotName);
            _logger.LogInformation("Swap complete!");
        }

        public override string GetDescription() =>
            $"Populates an App Setting called '{Configuration.SettingName}' in an Azure " +
            $"Functions application called {Configuration.ResourceName} (Resource Group " +
            $"'{Configuration.ResourceGroup}'). During the rekeying, the Functions App will " +
            $"be moved from slot '{Configuration.SourceSlot}' to slot '{Configuration.TemporarySlot}' " +
            $"temporarily, and then to slot '{Configuration.DestinationSlot}'.";

        private async Task ApplySecrets(string slotName, List<RegeneratedSecret> secrets)
        {
            if (secrets.Count > 1 && secrets.Select(s => s.UserHint).Distinct().Count() != secrets.Count)
            {
                throw new Exception("Multiple secrets sent to Provider but without distinct UserHints!");
            }

            IUpdate<IFunctionDeploymentSlot> updateBase = (await GetDeploymentSlot(slotName)).Update();
            foreach (RegeneratedSecret secret in secrets)
            {
                var appSettingName = string.IsNullOrEmpty(secret.UserHint) ? Configuration.SettingName : $"{Configuration.SettingName}-{secret.UserHint}";
                _logger.LogInformation("Updating AppSetting '{AppSettingName}' in slot '{SlotName}' (as {AppSettingType})", appSettingName, slotName,
                    Configuration.CommitAsConnectionString ? "connection string" : "secret");

                updateBase = updateBase.WithAppSetting(appSettingName,
                    Configuration.CommitAsConnectionString ? secret.NewConnectionStringOrKey.GetNormalString() : secret.NewSecretValue.GetNormalString());
            }

            _logger.LogInformation("Applying changes.");
            await updateBase.ApplyAsync();

            _logger.LogInformation("Swapping to '{SlotName}'", slotName);
            await (await GetFunctionsApp()).SwapAsync(slotName);
        }
    }
}
