using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PingWatch.Api;

// An MVC filter rather than IExceptionHandler: on .NET 9 the exception middleware
// logs every exception as unhandled, even ones a handler turns into a 400.
internal sealed class ValidationExceptionFilter(ProblemDetailsFactory problemDetails) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ValidationException validation)
        {
            return;
        }

        var errors = new ModelStateDictionary();
        foreach (var error in validation.Errors)
        {
            errors.AddModelError(JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName), error.ErrorMessage);
        }

        context.Result = new BadRequestObjectResult(
            problemDetails.CreateValidationProblemDetails(context.HttpContext, errors));
        context.ExceptionHandled = true;
    }
}
