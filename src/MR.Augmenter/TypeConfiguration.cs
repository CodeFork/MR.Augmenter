﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace MR.Augmenter
{
	public abstract class TypeConfiguration
	{
		public TypeConfiguration(Type type)
		{
			Type = type;
		}

		public Type Type { get; }

		internal List<Augment> Augments { get; } = new List<Augment>();

		public Dictionary<PropertyInfo, TypeConfiguration> NestedTypeConfigurations { get; }
			= new Dictionary<PropertyInfo, TypeConfiguration>();
	}

	public class TypeConfiguration<T> : TypeConfiguration
	{
		public TypeConfiguration()
			: base(typeof(T))
		{
		}

		public void ConfigureAdd(string name, Func<T, IReadOnlyDictionary<string, object>, object> valueFunc)
		{
			Augments.Add(new Augment(name, AugmentKind.Add, (obj, state) =>
			{
				var concrete = (T)obj;
				return valueFunc(concrete, state);
			}));
		}

		public void ConfigureRemove(string name, Func<T, IReadOnlyDictionary<string, object>, object> valueFunc = null)
		{
			Augments.Add(new Augment(name, AugmentKind.Remove, (obj, state) =>
			{
				var concrete = (T)obj;
				return valueFunc?.Invoke(concrete, state);
			}));
		}
	}
}