using NetArchTest.Rules;
using Shop.Api.Endpoints;
using Shop.Domain.Orders;

namespace Shop.Tests.Architecture;

public class ArchitectureTests
{
    private const string Domain = "Shop.Domain";
    private const string Application = "Shop.Application";
    private const string Infrastructure = "Shop.Infrastructure";
    private const string Api = "Shop.Api";

    private static readonly System.Reflection.Assembly DomainAssembly = typeof(Order).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly = typeof(Shop.Application.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly = typeof(Shop.Infrastructure.DependencyInjection).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly = typeof(OrderEndpoints).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_outside_itself()
    {
        TestResult result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Api, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_frameworks()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Api, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.IdentityModel")
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(Api)
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Endpoints_and_background_jobs_only_talk_to_use_cases()
    {
        PredicateList hosts = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespaceMatching(@"^Shop\.Api\.(Endpoints|BackgroundJobs)");
        Assert.NotEmpty(hosts.GetTypes());

        string[] ports = ApplicationAssembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Shop.Application.Abstractions")
            .Select(t => t.FullName!)
            .ToArray();
        Assert.NotEmpty(ports);

        TestResult result = hosts
            .ShouldNot()
            .HaveDependencyOnAny([Infrastructure, "Microsoft.EntityFrameworkCore", .. ports])
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void Dependency_checks_detect_real_dependencies()
    {
        TestResult infrastructureUsesEf = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        TestResult sweeperUsesUseCases = Types.InAssembly(ApiAssembly)
            .That()
            .ResideInNamespace("Shop.Api.BackgroundJobs")
            .And()
            .HaveNameEndingWith("Sweeper")
            .Should()
            .HaveDependencyOn("Shop.Application.Orders")
            .GetResult();

        Assert.False(infrastructureUsesEf.IsSuccessful);
        Assert.True(sweeperUsesUseCases.IsSuccessful);
    }

    private static void AssertSuccessful(TestResult result) =>
        Assert.True(result.IsSuccessful, "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []));
}
