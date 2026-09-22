using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.Uow;

namespace STNG.OpenIddict;

/* Creates initial data that is needed to property run the application
 * and make client-to-server communication possible.
 */
public class OpenIddictDataSeedContributor : OpenIddictDataSeedContributorBase, IDataSeedContributor, ITransientDependency
{
    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager)
        : base(configuration, openIddictApplicationRepository, applicationManager, openIddictScopeRepository, scopeManager)
    {
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
    }

    private async Task CreateScopesAsync()
    {
        await CreateScopesAsync(new OpenIddictScopeDescriptor 
        {
            Name = "STNG", 
            DisplayName = "STNG API", 
            Resources = { "STNG" }
        });
    }

    private async Task CreateApplicationsAsync()
    {
        await CreateOrUpdateApplicationAsync(
            applicationType: OpenIddictConstants.ApplicationTypes.Native,
            name: STNG.Authentication.ParentAuthenticationConsts.MobileClientId,
            type: OpenIddictConstants.ClientTypes.Public,
            consentType: OpenIddictConstants.ConsentTypes.Implicit,
            displayName: "STNG Parent Mobile",
            secret: null,
            grantTypes: new List<string> { OpenIddictConstants.GrantTypes.Password, OpenIddictConstants.GrantTypes.RefreshToken },
            scopes: new List<string> { OpenIddictConstants.Permissions.Scopes.Profile, OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddictConstants.Permissions.Scopes.Roles, "STNG" });

        var commonScopes = new List<string> {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "STNG"
        };

        var configurationSection = Configuration.GetSection("OpenIddict:Applications");


        // Console Test / Angular Client
        
        var appClientId = configurationSection["STNG_App:ClientId"];
        if (!appClientId.IsNullOrWhiteSpace() && !configurationSection["STNG_App:RootUrl"].IsNullOrWhiteSpace())
        {
            var appClientRootUrl = configurationSection["STNG_App:RootUrl"]!.TrimEnd('/');
            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: appClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Console Test / Angular Application",
                secret: null,
                grantTypes: new List<string> {
                    OpenIddictConstants.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.GrantTypes.RefreshToken,
                },
                scopes: commonScopes,
                redirectUris: new List<string> { appClientRootUrl },
                postLogoutRedirectUris: new List<string> { appClientRootUrl },
                clientUri: appClientRootUrl,
                logoUri: "/images/clients/angular.svg"
            );
        }

        
        




        // Swagger Client
        var swaggerClientId = configurationSection["STNG_Swagger:ClientId"];
        if (!swaggerClientId.IsNullOrWhiteSpace() && !configurationSection["STNG_Swagger:RootUrl"].IsNullOrWhiteSpace())
        {
            var swaggerRootUrl = configurationSection["STNG_Swagger:RootUrl"]!.TrimEnd('/');

            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: swaggerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Swagger Application",
                secret: null,
                grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode, },
                scopes: commonScopes,
                redirectUris: new List<string> { $"{swaggerRootUrl}/swagger/oauth2-redirect.html" },
                clientUri: swaggerRootUrl.EnsureEndsWith('/') + "swagger",
                logoUri: "/images/clients/swagger.svg"
            );
        }


    }
}
