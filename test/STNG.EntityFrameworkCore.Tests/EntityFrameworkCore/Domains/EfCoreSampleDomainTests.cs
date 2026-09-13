using STNG.Samples;
using Xunit;

namespace STNG.EntityFrameworkCore.Domains;

[Collection(STNGTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<STNGEntityFrameworkCoreTestModule>
{

}
