using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp1.Shared;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rocket.Surgery.Conventions;
using Rocket.Surgery.Extensions.DependencyInjection;

[assembly: Convention(typeof(Convention))]

namespace BlazorApp1.Shared
{
    public class WeatherForecast
    {
        public DateTime Date { get; set; }

        public int TemperatureC { get; set; }

        public string Summary { get; set; }

        public int TemperatureF => 32 + (int) ( TemperatureC / 0.5556 );
    }

    public static class DoStuff
    {
        public class Request : IRequest<Response>
        {
            public string Echo { get; set; }
        }

        public class Response
        {
            public string Pong { get; set; }
        }

        class Validator : AbstractValidator<Request>
        {
            public Validator()
            {
                RuleFor(x => x.Echo)
                    .NotEmpty();
            }
        }

        class Handler : IRequestHandler<Request, Response>
        {
            private readonly ISomeService _service;

            public Handler(ISomeService service)
            {
                _service = service;
            }
            public Task<Response> Handle(Request request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new Response() { Pong = _service.Value + request.Echo });
            }
        }
    }

    public interface ISomeService
    {
        string Value { get; }
    }

    class SomeService : ISomeService
    {
        public SomeService(string value)
        {
            Value = value;
        }
        public string Value { get; }
    }

    class Convention : IServiceConvention
    {
        public void Register(IServiceConventionContext context)
        {
            context.Services.AddSingleton<Shared.ISomeService>(new SomeService("Hello "));
        }
    }
}
