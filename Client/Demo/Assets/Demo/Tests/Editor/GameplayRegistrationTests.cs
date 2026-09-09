using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Assets.Scripts.DE.Client.Framework;
using Demo.Client.Application;
using NUnit.Framework;
using UnityEngine;

namespace Demo.Client.Tests
{
    public class GameplayRegistrationTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetRegistration();
        }

        [TearDown]
        public void TearDown()
        {
            var reset = typeof(ApplicationRoot).GetMethod(
                "ResetGameplayRegistration", BindingFlags.NonPublic | BindingFlags.Static);
            reset?.Invoke(null, null);
        }

        [Test]
        public void RegistrationDefersConstruction()
        {
            int createCount = 0;
            var expected = new TestGameInstance();
            Register(typeof(TestGameInstance).Assembly, () =>
            {
                createCount++;
                return expected;
            });

            Assert.That(createCount, Is.Zero);
            Assert.That(GetFactory()(), Is.SameAs(expected));
            Assert.That(createCount, Is.EqualTo(1));
        }

        [Test]
        public void InvalidArgumentsDoNotOccupyRegistration()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Register(null, () => new TestGameInstance()));
            Assert.Throws<ArgumentNullException>(() =>
                Register(typeof(TestGameInstance).Assembly, null));

            Assert.DoesNotThrow(() =>
                Register(typeof(TestGameInstance).Assembly, () => new TestGameInstance()));
        }

        [Test]
        public void DuplicateRegistrationDoesNotReplaceTheOriginalFactory()
        {
            var first = new TestGameInstance();
            Register(typeof(TestGameInstance).Assembly, () => first);

            Assert.Throws<InvalidOperationException>(() =>
                Register(typeof(DemoGameInstance).Assembly, () => new DemoGameInstance()));

            Assert.That(GetFactory()(), Is.SameAs(first));
        }

        [Test]
        public void ResetAllowsRegistrationForTheNextSession()
        {
            Register(typeof(TestGameInstance).Assembly, () => new TestGameInstance());
            ResetRegistration();

            Assert.That(GetFactory(), Is.Null);
            Assert.DoesNotThrow(() =>
                Register(typeof(DemoGameInstance).Assembly, () => new DemoGameInstance()));
            Assert.That(GetFactory()(), Is.TypeOf<DemoGameInstance>());
        }

        [Test]
        public void DemoBootstrapRegistersBeforeAwakeAndAfterReset()
        {
            var bootstrapType = typeof(DemoGameInstance).Assembly.GetType(
                "Demo.Client.Application.DemoBootstrap");
            Assert.That(bootstrapType, Is.Not.Null, "Demo bootstrap is missing.");

            var register = bootstrapType.GetMethod(
                "RegisterGameplay", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(register, Is.Not.Null);

            var reset = GetRootMethod("ResetGameplayRegistration");
            Assert.That(reset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType,
                Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
            Assert.That(register.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType,
                Is.EqualTo(RuntimeInitializeLoadType.BeforeSceneLoad));

            Invoke(register);
            Assert.That(GetFactory()(), Is.TypeOf<DemoGameInstance>());

            var assemblyField = typeof(ApplicationRoot).GetField(
                "_RegisteredGameplayAssembly", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(assemblyField.GetValue(null), Is.SameAs(typeof(DemoGameInstance).Assembly));
        }

        private static void Register(Assembly assembly, Func<GameInstance> factory)
        {
            Invoke(GetRootMethod("RegisterGameplay"), assembly, factory);
        }

        private static void ResetRegistration()
        {
            Invoke(GetRootMethod("ResetGameplayRegistration"));
        }

        private static Func<GameInstance> GetFactory()
        {
            var field = typeof(ApplicationRoot).GetField(
                "_GameInstanceFactory", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "GameInstance factory registration is missing.");
            return (Func<GameInstance>)field.GetValue(null);
        }

        private static MethodInfo GetRootMethod(string name)
        {
            var method = typeof(ApplicationRoot).GetMethod(
                name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, name + " is missing.");
            return method;
        }

        private static void Invoke(MethodInfo method, params object[] arguments)
        {
            try
            {
                method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        private sealed class TestGameInstance : GameInstance
        {
        }
    }
}
