using AspNetFixture.Services;

var builder = WebApplication.CreateBuilder(args);

// 지원하는 DI 등록: <TService, TImplementation>과 <TService> 제네릭 오버로드.
builder.Services.AddSingleton<IOrderService, OrderService>();
builder.Services.AddScoped<OrderNotifier>();

// 지원하지 않는 DI 등록: 팩터리 대리자 오버로드. DependencyRegistration
// 노드를 만들면 안 된다(plan §3.3).
builder.Services.AddTransient<OrderNotifier>(_ => new OrderNotifier());

var app = builder.Build();

// 메서드 그룹 처리기: Handlers.GetOrderHandler로 해소되고 RoutesTo 엣지를 얻는다.
app.MapGet("/orders/{id}", AspNetFixture.Handlers.GetOrderHandler);

// 람다 처리기: 람다 자신의 NodeKind.Function 노드로 가는 RoutesTo 엣지를 얻는다
// (plan §5.7). 람다 본문 안의 호출은 감싸는 메서드가 아니라 그 람다 노드에
// 귀속되므로, 흐름이 끝까지 도달할 수 있다(plan §9.4).
app.MapPost("/orders", (AspNetFixture.Services.IOrderService orders) => orders.GetOrder(0));

// 상수 동사 배열을 쓰는 MapMethods.
app.MapMethods("/orders/{id}/status", new[] { "GET", "HEAD" }, AspNetFixture.Handlers.GetOrderHandler);

// 오버로드된 메서드 그룹 처리기: 맨 메서드 그룹 인자는 여기서 모호하다(CS1503).
// 그래서 호출 지점은 명시적 대리자 변환으로 오버로드를 고정한다. Roslyn은 그래도
// 이것을 실제의 단일 IMethodSymbol(매개변수 없는 오버로드)로 해소하며, 그 형태는
// AnalyzeMinimalApi가 다른 메서드 그룹 처리기에서 보는 것과 같다. RoutesTo 엣지는
// 정확한 심볼 동일성으로 바로 그 오버로드에 해소되어야 한다(plan §5.6). 이름/한정 이름
// 일치만으로는 구별할 수 없다. 두 오버로드가 같은 선언 타입과
// 메서드 이름을 공유하기 때문이다.
app.MapGet("/ping", (Func<string>)AspNetFixture.Handlers.PingHandler);

// 상수가 아닌 경로 템플릿. Route 노드를 만들면 안 된다.
var dynamicSegment = Environment.GetEnvironmentVariable("SEGMENT") ?? "dynamic";
app.MapGet("/" + dynamicSegment, () => "no route node expected");

app.Run();
