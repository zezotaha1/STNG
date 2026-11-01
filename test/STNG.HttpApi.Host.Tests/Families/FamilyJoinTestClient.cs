using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using STNG.Families;

namespace STNG;

internal sealed class FamilyJoinTestClient
{
    internal FamilyTestClient Core { get; }
    internal FamilyJoinTestClient(AuthenticationHostFixture host) => Core = new(host);
    internal async Task<T> PostAsync<T>(FamilyTestActor actor, string path, object body)
    {
        var response = await Core.SendAsync(actor, HttpMethod.Post, path, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    internal Task<IssuedFamilyJoinCredentialDto> IssueAsync(FamilyTestActor owner, Guid family,
        FamilyJoinCredentialKind kind = FamilyJoinCredentialKind.ManualCode, Guid? id = null) =>
        PostAsync<IssuedFamilyJoinCredentialDto>(owner, $"/api/families/{family}/join-credentials",
            new CreateFamilyJoinCredentialDto { RequestId = id ?? Guid.NewGuid(), Kind = kind });
    internal Task<FamilyJoinRequestDto> SubmitAsync(FamilyTestActor applicant, IssuedFamilyJoinCredentialDto credential, Guid? id = null) =>
        PostAsync<FamilyJoinRequestDto>(applicant, "/api/family-join-requests", new SubmitFamilyJoinRequestDto
        { RequestId = id ?? Guid.NewGuid(), Kind = credential.Credential.Kind, Secret = credential.Secret! });
    internal Task<FamilyJoinRequestDto> DecideAsync(FamilyTestActor actor, FamilyJoinRequestDto request, string decision) =>
        PostAsync<FamilyJoinRequestDto>(actor, $"/api/families/{request.FamilyId}/join-requests/{request.Id}/{decision}",
            new FamilyMutationDto { ConcurrencyStamp = request.ConcurrencyStamp });
    internal Task<FamilyJoinRequestDto> CancelAsync(FamilyTestActor actor, FamilyJoinRequestDto request) =>
        PostAsync<FamilyJoinRequestDto>(actor, $"/api/family-join-requests/{request.Id}/cancel",
            new FamilyMutationDto { ConcurrencyStamp = request.ConcurrencyStamp });
}
