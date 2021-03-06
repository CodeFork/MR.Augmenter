﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MR.Augmenter
{
	public class AugmenterActionFilterAttribute : TypeFilterAttribute
	{
		public AugmenterActionFilterAttribute() : base(typeof(AugmenterActionFilterImpl))
		{
		}

		private class AugmenterActionFilterImpl : IAsyncResultFilter
		{
			private IAugmenter _augmenter;

			public AugmenterActionFilterImpl(IAugmenter augmenter)
			{
				_augmenter = augmenter;
			}

			public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
			{
				if (context.Result is ViewResult)
				{
					return next();
				}

				if (context.Result is ObjectResult objectResult)
				{
					return OnResultExecutionCoreAsync(context, next, objectResult.Value, v => objectResult.Value = v);
				}

				if (context.Result is JsonResult jsonResult)
				{
					return OnResultExecutionCoreAsync(context, next, jsonResult.Value, v => jsonResult.Value = v);
				}

				return next();
			}

			private async Task OnResultExecutionCoreAsync(
				ResultExecutingContext context,
				ResultExecutionDelegate next,
				object value,
				Action<object> setResultObject)
			{
				var augmented = await _augmenter.AugmentAsync(value);
				setResultObject(augmented);
				await next();
			}
		}
	}
}
