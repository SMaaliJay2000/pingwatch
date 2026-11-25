using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PingWatch.Application.Monitors;

namespace PingWatch.Api;

// An MVC filter rather than IExceptionHandler: on .NET 9 the exception middleware
// logs every exception as unhandled, even ones a handler turns into a 4xx.
internal sealed class ApiExceptionFilter(ProblemDetailsFactory problemDetails) : IExceptionFilter
{
    private const string CodeKey = "code";

    public void OnException(ExceptionContext context)
    {
        var problem = context.Exception switch
        {
            ValidationException validation => ValidationProblem(context.HttpContext, validation),
            PlanLimitException limit => PlanLimitProblem(context.HttpContext, limit),
            _ => null,
        };

        if (problem is null)
        {
            return;
        }

        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        context.ExceptionHandled = true;
    }

    private ProblemDetails ValidationProblem(HttpContext httpContext, ValidationException validation)
    {
        var errors = new ModelStateDictionary();
        foreach (var error in validation.Errors)
        {
            errors.AddModelError(JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName), error.ErrorMessage);
        }

        var problem = problemDetails.CreateValidationProblemDetails(httpContext, errors);

        if (validation.Errors.Any(e => e.ErrorCode == PlanLimitException.Code))
        {
            problem.Extensions[CodeKey] = PlanLimitException.Code;
        }

        return problem;
    }

    private ProblemDetails PlanLimitProblem(HttpContext httpContext, PlanLimitException limit)
    {
        var problem = problemDetails.CreateProblemDetails(
            httpContext, StatusCodes.Status403Forbidden, title: "Plan limit reached", detail: limit.Message);
        problem.Extensions[CodeKey] = PlanLimitException.Code;

        return problem;
    }
}