﻿using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MR.Augmenter
{
	public class JsonAugmenterTest : CommonTestHost
	{
		public class AugmentMethod : JsonAugmenterTest
		{
			private AugmenterConfiguration _configuration;
			private JsonAugmenter _fixture;

			public AugmentMethod()
			{
				_configuration = ConfigureCommon();
				_fixture = MocksHelper.JsonAugmenter(_configuration);
			}

			[Fact]
			public async Task Basic()
			{
				var model = new TestModel1();

				var result = await _fixture.AugmentAsync(model) as JObject;

				result["Bar"].Value<string>().Should().Be($"({model.Id})");
				result[nameof(TestModel1.Some)].Should().BeNull();
			}

			[Fact]
			public async Task Basic_WithLocal()
			{
				var model = new TestModel1();

				var result = await _fixture.AugmentAsync(model, c =>
				{
					c.ConfigureAdd("Baz", (_, __) => 2);
				}) as JObject;

				result["Bar"].Value<string>().Should().Be($"({model.Id})");
				result["Baz"].Value<int>().Should().Be(2);
				result[nameof(TestModel1.Some)].Should().BeNull();
			}

			[Fact]
			public async Task Add_WithIgnore_IgnoresAugment()
			{
				var model = new TestModel1();

				var result = await _fixture.AugmentAsync(model, c =>
				{
					c.ConfigureAdd("Some", (_, __) => AugmentationValue.Ignore);
				}) as JObject;

				result["Some"].Should().BeNull();
			}

			[Fact]
			public async Task Remove_WithIgnore_IgnoresAugment()
			{
				var model = new TestModel1();

				var result = await _fixture.AugmentAsync(model, c =>
				{
					c.ConfigureRemove(nameof(TestModel1.Foo), (_, __) => AugmentationValue.Ignore);
				}) as JObject;

				result.Value<string>(nameof(TestModel1.Foo)).Should().Be("foo");
			}

			public class Nested : JsonAugmenterTest
			{
				private AugmenterConfiguration _configuration;
				private JsonAugmenter _fixture;

				public Nested()
				{
					_configuration = new AugmenterConfiguration();
					_configuration.Configure<TestModelWithNested>(c =>
					{
						c.ConfigureAdd("Foo", (_, __) => "42");
					});
					_configuration.Configure<TestModelNested>(c =>
					{
						c.ConfigureAdd("Foo", (_, __) => "43");
					});
					_configuration.Configure<TestModelNestedNested>(c =>
					{
						c.ConfigureAdd("Foo", (_, __) => "44");
					});

					_fixture = MocksHelper.JsonAugmenter(_configuration);
				}

				[Fact]
				public async Task Basic()
				{
					var model = new TestModelWithNested();

					var result = await _fixture.AugmentAsync(model) as JObject;

					result.Value<string>("Foo").Should().Be("42");
					var nested = result.Value<JObject>("Nested");
					nested.Value<string>("Foo").Should().Be("43");
					var nestedNested = nested.Value<JObject>("Nested");
					nestedNested.Value<string>("Foo").Should().Be("44");
				}
			}

			public class State : JsonAugmenterTest
			{
				private AugmenterConfiguration _configuration;
				private JsonAugmenter _fixture;

				public State()
				{
					_configuration = new AugmenterConfiguration();
					_fixture = MocksHelper.JsonAugmenter(_configuration);
				}

				[Fact]
				public async Task Globally()
				{
					var model = new TestModel1();

					_configuration = new AugmenterConfiguration();
					_configuration.Configure<TestModel1>(c =>
					{
						c.ConfigureAdd("Bar", (x, state) =>
						{
							return state["key"];
						});
					});
					_fixture = MocksHelper.JsonAugmenter(_configuration);

					var result = await _fixture.AugmentAsync(model, addState: state =>
					{
						state.Add("key", "bar");
					}) as JObject;

					result.Value<string>("Bar").Should().Be("bar");
				}

				[Fact]
				public async Task Locally()
				{
					var model = new TestModel1();

					var result = await _fixture.AugmentAsync(model, c =>
					{
						c.ConfigureAdd("Bar", (x, state) =>
						{
							return state["key"];
						});
					}, state =>
					{
						state.Add("key", "bar");
					}) as JObject;

					result.Value<string>("Bar").Should().Be("bar");
				}

				[Fact]
				public async Task Nested()
				{
					var model = new TestModelWithNested();

					_configuration = new AugmenterConfiguration();
					_configuration.Configure<TestModelWithNested>(null);
					_configuration.Configure<TestModelNested>(c =>
					{
						c.ConfigureAdd("Foo", (x, state) =>
						{
							return state["key"];
						});
					});
					_fixture = MocksHelper.JsonAugmenter(_configuration);

					var result = await _fixture.AugmentAsync(model, addState: state =>
					{
						state.Add("key", "foo");
					}) as JObject;

					result.Value<JObject>("Nested").Value<string>("Foo").Should().Be("foo");
				}

				[Fact]
				public async Task WithIgnore()
				{
					var model = new TestModel1();

					var result = await _fixture.AugmentAsync(model, config =>
					{
						config.ConfigureAdd("Bar", (x, state) =>
						{
							if ((bool)state["key"])
							{
								return "YES";
							}
							return AugmentationValue.Ignore;
						});
					}, state =>
					{
						state.Add("key", false);
					}) as JObject;

					result["Bar"].Should().BeNull();
				}
			}
		}
	}
}