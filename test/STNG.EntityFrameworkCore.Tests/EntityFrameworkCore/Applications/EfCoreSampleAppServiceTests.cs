using STNG.Samples;
using Xunit;

namespace STNG.EntityFrameworkCore.Applications;

[Collection(STNGTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<STNGEntityFrameworkCoreTestModule>
{

}
