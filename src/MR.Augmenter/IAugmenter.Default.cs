﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AArray = System.Collections.Generic.List<object>;
using AObject = System.Collections.Generic.Dictionary<string, object>;

namespace MR.Augmenter
{
	/// <summary>
	/// Default implementation of <see cref="IAugmenter"/>.
	/// Transforms objects to <see cref="IDictionary{TKey, TValue}"/> when necessary.
	/// </summary>
	public class Augmenter : AugmenterBase
	{
		public Augmenter(
			AugmenterConfiguration configuration,
			IServiceProvider services)
			: base(configuration, services)
		{
		}

		protected override object AugmentCore(AugmentationContext context)
		{
			var obj = context.Object;
			var root = new AObject();

			foreach (var typeConfiguration in BuildList(context.TypeConfiguration, context.EphemeralTypeConfiguration))
			{
				AugmentObject(obj, typeConfiguration, root, context.State);
			}

			return root;
		}

		private void AugmentObject(object obj, TypeConfiguration typeConfiguration, AObject root, IReadOnlyState state)
		{
			foreach (var property in typeConfiguration.Properties)
			{
				var nestedObject = property.GetValue(obj);
				if (property.TypeConfiguration == null)
				{
					// This should be copied verbatim.
					// REVIEW: Should we check if it's wrapped and unwrap it?

					root[property.PropertyInfo.Name] = nestedObject;
				}
				else
				{
					if (nestedObject == null)
					{
						root[property.PropertyInfo.Name] = null;
					}
					else if (!property.TypeInfoWrapper.IsArray)
					{
						var ntc = GetNestedTypeConfiguration(typeConfiguration, property);
						var done = false;
						AugmenterWrapper wrapper = null;
						if (property.TypeInfoWrapper.IsWrapper)
						{
							wrapper = nestedObject as AugmenterWrapper;
							nestedObject = wrapper.Object;

							if (typeof(IEnumerable).GetTypeInfo().IsAssignableFrom(nestedObject.GetType().GetTypeInfo()))
							{
								// The nested object is an array. This means we should apply the
								// wrapper's configuration to all items in the array.

								var nestedList = (nestedObject as IList) != null ?
									new AArray((nestedObject as IList).Count) :
									new AArray();
								AugmentArray(obj, ntc, nestedObject, property, nestedList, state,
									BuildList(property.TypeConfiguration, wrapper.TypeConfiguration));
								root[property.PropertyInfo.Name] = nestedList;
								done = true;
							}
						}

						if (!done)
						{
							var nestedDict = new AObject();
							foreach (var tc in BuildList(property.TypeConfiguration, ntc?.TypeConfiguration))
							{
								var nestedState = CreateNestedState(obj, ntc, state);
								AugmentObject(nestedObject, tc, nestedDict, nestedState);
							}
							root[property.PropertyInfo.Name] = nestedDict;
						}
					}
					else
					{
						var ntc = GetNestedTypeConfiguration(typeConfiguration, property);
						var nestedList = new AArray();
						AugmentArray(obj, ntc, nestedObject, property, nestedList, state, BuildList(property.TypeConfiguration, ntc?.TypeConfiguration));
						root[property.PropertyInfo.Name] = nestedList;
					}
				}
			}

			foreach (var augment in typeConfiguration.Augments)
			{
				ApplyAugment(obj, root, augment, state);
			}
		}

		private void AugmentArray(
			object obj,
			NestedTypeConfiguration ntc,
			object arr,
			APropertyInfo property,
			AArray nestedList,
			IReadOnlyState state,
			List<TypeConfiguration> typeConfigurations)
		{
			var asEnumerable = arr as IEnumerable;
			foreach (var item in asEnumerable)
			{
				object actual = item;
				if (property.TypeInfoWrapper.IsWrapper)
				{
					var wrapper = item as AugmenterWrapper;
					if (wrapper != null)
					{
						actual = wrapper.Object;
					}
				}

				var dict = new AObject();
				foreach (var tc in typeConfigurations)
				{
					var nestedState = CreateNestedState(obj, ntc, state);
					AugmentObject(actual, tc, dict, nestedState);
				}
				nestedList.Add(dict);
			}
		}

		private NestedTypeConfiguration GetNestedTypeConfiguration(
			TypeConfiguration typeConfiguration, APropertyInfo property)
		{
			if (!typeConfiguration.NestedConfigurations.IsValueCreated)
			{
				return null;
			}

			NestedTypeConfiguration ntc;
			if (!typeConfiguration.NestedConfigurations.Value.TryGetValue(property.PropertyInfo, out ntc))
			{
				return null;
			}

			return ntc;
		}

		private IReadOnlyState CreateNestedState(object obj, NestedTypeConfiguration ntc, IReadOnlyState state)
		{
			if (ntc?.AddState == null)
			{
				return state;
			}

			var addedState = new State();
			ntc.AddState(obj, state, addedState);
			var newState = new State(state, addedState);
			return new ReadOnlyState(newState);
		}

		private void ApplyAugment(object obj, AObject root, Augment augment, IReadOnlyState state)
		{
			switch (augment.Kind)
			{
				case AugmentKind.Add:
					ApplyAddAugment(obj, root, augment, state);
					break;

				case AugmentKind.Remove:
					ApplyRemoveAugment(obj, root, augment, state);
					break;
			}
		}

		private void ApplyAddAugment(object obj, AObject root, Augment augment, IReadOnlyState state)
		{
			var value = augment.ValueFunc(obj, state);
			if (ShouldIgnoreAugment(value))
			{
				return;
			}

			root[augment.Name] = augment.ValueFunc(obj, state);
		}

		private void ApplyRemoveAugment(object obj, AObject root, Augment augment, IReadOnlyState state)
		{
			var value = augment.ValueFunc?.Invoke(obj, state);
			if (ShouldIgnoreAugment(value))
			{
				return;
			}

			root.Remove(augment.Name);
		}

		private List<TypeConfiguration> BuildList(TypeConfiguration typeConfiguration, TypeConfiguration ephemeral)
		{
			var list = new List<TypeConfiguration>();

			if (typeConfiguration != null)
			{
				foreach (var tc in typeConfiguration.BaseTypeConfigurations)
				{
					list.Add(tc);
				}

				list.Add(typeConfiguration);
			}

			if (ephemeral != null)
			{
				list.Add(ephemeral);
			}

			return list;
		}
	}
}
