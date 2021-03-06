﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MR.Augmenter.Internal;

namespace MR.Augmenter
{
	/// <summary>
	/// The base class of <see cref="IAugmenter"/>.
	/// </summary>
	public abstract class AugmenterBase : IAugmenter
	{
		private readonly ConcurrentDictionary<Type, TypeConfiguration> _cache
			= new ConcurrentDictionary<Type, TypeConfiguration>();

		private readonly Task<object> _nullResultTask = Task.FromResult((object)null);
		private readonly TypeConfigurationBuilder _builder;

		protected AugmenterBase(
			IOptions<AugmenterConfiguration> configuration,
			IServiceProvider services)
		{
			if (!configuration.Value.Built)
			{
				throw new InvalidOperationException("AugmenterConfiguration should be built first using Build().");
			}

			Configuration = configuration.Value;
			Services = services;
			_builder = new TypeConfigurationBuilder(Configuration.TypeConfigurations);
		}

		public AugmenterConfiguration Configuration { get; }

		public IServiceProvider Services { get; }

		public virtual Task<object> AugmentAsync<T>(
			T obj,
			Action<TypeConfiguration<T>> configure = null,
			Action<IState> addState = null)
		{
			return AugmentCommonAsync(obj, configure, addState);
		}

		public Task<object> AugmentAsync<T>(
			IEnumerable<T> obj,
			Action<TypeConfiguration<T>> configure = null,
			Action<IState> addState = null)
		{
			return AugmentCommonAsync(obj, configure, addState);
		}

		// This method exists in order to make calls to AugmentAsync with an array select
		// the right generic method (the enumerable one).
		public Task<object> AugmentAsync<T>(
			T[] obj,
			Action<TypeConfiguration<T>> configure = null,
			Action<IState> addState = null)
		{
			return AugmentAsync((IEnumerable<T>)obj, configure, addState);
		}

		private Task<object> AugmentCommonAsync<T>(
			object obj,
			Action<TypeConfiguration<T>> configure,
			Action<IState> addState)
		{
			if (obj == null)
			{
				return _nullResultTask;
			}

			var type = obj.GetType();

			if (configure == null)
			{
				return AugmentInternal(
					obj, type,
					addState,
					null,
					null);
			}
			else
			{
				return AugmentInternal(
					obj, type,
					addState,
					(context, state) =>
					{
						var c = state as Action<TypeConfiguration<T>>;
						var ephemeralTypeConfigration = new TypeConfiguration<T>();
						Debug.Assert(c != null, "c != null");
						c(ephemeralTypeConfigration);
						context.EphemeralTypeConfiguration = ephemeralTypeConfigration;
					},
					configure);
			}
		}

		private async Task<object> AugmentInternal(
			object obj, Type type,
			Action<IState> addState,
			Action<AugmentationContext, object> configure,
			object configureState)
		{
			var tiw = TypeInfoResolver.ResolveTypeInfo(type);
			if (tiw.IsPrimitive)
			{
				return obj;
			}

			var alwaysBuild = tiw.IsWrapper || configure != null;
			var typeConfiguration = ResolveTypeConfiguration(tiw.Type, alwaysBuild);

			if (typeConfiguration == null && configure == null)
			{
				// No configuration
				return obj;
			}

			var state = await CreateDictionaryAndAddStateAsync(addState, obj);
			var context = new AugmentationContext(obj, typeConfiguration, state);

			if (tiw.IsArray)
			{
				var asEnumerable = obj as IEnumerable;
				var list = (obj as IList) != null ? new List<object>((obj as IList).Count) : new List<object>();
				Debug.Assert(asEnumerable != null, "asEnumerable != null");
				foreach (var item in asEnumerable)
				{
					// We'll reuse the context.
					context.Object = item;
					list.Add(AugmentOne(obj, configure, configureState, tiw, context));
				}
				return list;
			}
			else if (tiw.IsWrapper &&
				ReflectionHelper.IsEnumerableOrArrayType((((AugmenterWrapper)obj).Object).GetType(), out var elementType))
			{
				context.EphemeralTypeConfiguration = ((AugmenterWrapper)obj).TypeConfiguration;
				obj = (((AugmenterWrapper)obj).Object);
				tiw = TypeInfoResolver.ResolveTypeInfo(elementType);
				var asEnumerable = obj as IEnumerable;
				var list = (obj as IList) != null ? new List<object>((obj as IList).Count) : new List<object>();
				foreach (var item in asEnumerable)
				{
					// We'll reuse the context.
					context.Object = item;
					list.Add(AugmentOne(obj, configure, configureState, tiw, context));
				}
				return list;
			}
			else
			{
				return AugmentOne(obj, configure, configureState, tiw, context);
			}
		}

		private object AugmentOne(
			object obj,
			Action<AugmentationContext, object> configure,
			object configureState,
			TypeInfoWrapper tiw,
			AugmentationContext context)
		{
			if (tiw.IsWrapper)
			{
				context.Object = ((AugmenterWrapper)obj).Object;
				context.EphemeralTypeConfiguration = ((AugmenterWrapper)obj).TypeConfiguration;
			}

			configure?.Invoke(context, configureState);

			return AugmentCore(context);
		}

		private async Task<IReadOnlyState> CreateDictionaryAndAddStateAsync(Action<IState> addState, object obj)
		{
			var state = new State();

			var task = Configuration.ConfigureGlobalState?.Invoke(state, Services);
			if (task != null)
			{
				await task;
			}

			addState?.Invoke(state);

			var wrapper = obj as AugmenterWrapper;
			wrapper?.AddState?.Invoke(wrapper.Object, state);

			return new ReadOnlyState(state);
		}

		private TypeConfiguration ResolveTypeConfiguration(Type type, bool alwaysBuild)
		{
			return _cache.GetOrAdd(type, t =>
			{
				var typeConfiguration = Configuration.TypeConfigurations
					.FirstOrDefault(c => type == c.Type);

				// ReSharper disable once ConvertIfStatementToNullCoalescingExpression
				if (typeConfiguration == null)
				{
					typeConfiguration = _builder.Build(null, type, alwaysBuild);
				}

				return typeConfiguration;
			});
		}

		protected abstract object AugmentCore(AugmentationContext context);

		protected bool ShouldIgnoreAugment(object value)
		{
			return value == AugmentationValue.Ignore;
		}
	}
}
