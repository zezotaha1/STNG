using Xunit;

namespace STNG.EntityFrameworkCore;

[CollectionDefinition(STNGTestConsts.CollectionDefinitionName)]
public class STNGEntityFrameworkCoreCollection : ICollectionFixture<STNGEntityFrameworkCoreFixture>
{

}
