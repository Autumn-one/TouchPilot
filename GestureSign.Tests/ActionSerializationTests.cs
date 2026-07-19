using System.Collections.Generic;
using GestureSign.Common.Input;
using Newtonsoft.Json;
using Xunit;
using GestureAction = GestureSign.Common.Applications.Action;

namespace GestureSign.Tests
{
    public class ActionSerializationTests
    {
        [Fact]
        public void EmptyEdgeBindingsDoNotChangeExistingActionJson()
        {
            var action = new GestureAction { Name = "Existing" };

            string json = JsonConvert.SerializeObject(action, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            });

            Assert.DoesNotContain(nameof(GestureAction.EdgeGestures), json);
        }

        [Fact]
        public void EdgeBindingsRoundTrip()
        {
            var action = new GestureAction
            {
                Name = "Volume",
                EdgeGestures = new List<FixedEdgeGesture> { FixedEdgeGesture.LeftSlideUp }
            };

            string json = JsonConvert.SerializeObject(action);
            GestureAction restored = JsonConvert.DeserializeObject<GestureAction>(json);

            Assert.Equal(FixedEdgeGesture.LeftSlideUp, Assert.Single(restored.EdgeGestures));
        }
    }
}
