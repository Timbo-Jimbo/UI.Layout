#if TJ_LAYOUT_PROPERTY_BINDINGS
using System;
using TimboJimbo.PropertyBindings;
using TimboJimbo.PropertyBindings.Bindings;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Property descriptors for <see cref="LayoutNode"/>, so the Sequencer, Styling and any bindings user drive
    /// a node through its setters: <see cref="Offset"/> moves the node without a pass, everything else re-lays
    /// the tree out. Present only while the Property Bindings package is installed.
    /// </summary>
    public static class LayoutNodeProperties
    {
        public static readonly PropertyDescriptor<LayoutNode, Vector2> Offset = Vector2Descriptor("timbojimbo.layoutNode.offset", "_offset");
        public static readonly PropertyDescriptor<LayoutNode, Vector2> FloatOffset = Vector2Descriptor("timbojimbo.layoutNode.floatOffset", "_floatOffset");
        public static readonly PropertyDescriptor<LayoutNode, Vector4> Padding = new(
            "timbojimbo.layoutNode.padding", "_padding", ValueKind.Vector4, ComponentLayout.Four,
            "_padding.x", "_padding.y", "_padding.z", "_padding.w");
        public static readonly PropertyDescriptor<LayoutNode, float> Gap = Scalar<float>("timbojimbo.layoutNode.gap", "_gap", ValueKind.Float);
        public static readonly PropertyDescriptor<LayoutNode, float> AspectRatio = Scalar<float>("timbojimbo.layoutNode.aspectRatio", "_aspectRatio", ValueKind.Float);
        public static readonly PropertyDescriptor<LayoutNode, float> WidthValue = Scalar<float>("timbojimbo.layoutNode.width.value", "_width.Value", ValueKind.Float);
        public static readonly PropertyDescriptor<LayoutNode, float> HeightValue = Scalar<float>("timbojimbo.layoutNode.height.value", "_height.Value", ValueKind.Float);
        public static readonly PropertyDescriptor<LayoutNode, LayoutDirection> Direction = Scalar<LayoutDirection>("timbojimbo.layoutNode.direction", "_direction", ValueKind.Enum);
        public static readonly PropertyDescriptor<LayoutNode, AlignX> AlignX = Scalar<AlignX>("timbojimbo.layoutNode.alignX", "_alignX", ValueKind.Enum);
        public static readonly PropertyDescriptor<LayoutNode, AlignY> AlignY = Scalar<AlignY>("timbojimbo.layoutNode.alignY", "_alignY", ValueKind.Enum);

        internal static readonly IPropertyDescriptor[] All =
        {
            Offset, FloatOffset, Padding, Gap, AspectRatio, WidthValue, HeightValue, Direction, AlignX, AlignY,
        };

        private static PropertyDescriptor<LayoutNode, Vector2> Vector2Descriptor(string id, string path) => new(
            id, path, ValueKind.Vector2, ComponentLayout.Two, $"{path}.x", $"{path}.y");

        private static PropertyDescriptor<LayoutNode, T> Scalar<T>(string id, string path, ValueKind kind) => new(
            id, path, kind, ComponentLayout.One, path);
    }

    /// <summary>Reads and writes <see cref="LayoutNode"/> properties through their setters.</summary>
    public sealed class LayoutNodePropertyBinding : OptimizedReadWritePropertyBinding
    {
        private LayoutNode _node;
        private readonly Property _property;

        public LayoutNodePropertyBinding(GameObject root, BindableProperty property)
            : base(root, OptimizationConfig.Moderate)
        {
            if (!TryResolve(property, out _node, out _property))
                throw new ArgumentException($"{nameof(LayoutNodePropertyBinding)} does not support property '{property.DescriptorId}'.", nameof(property));
        }

        private static bool TryResolve(BindableProperty property, out LayoutNode node, out Property resolved)
        {
            node = property.Target as LayoutNode;
            resolved = default;
            if (node == null) return false;

            if (LayoutNodeProperties.Offset.Matches(property)) resolved = Property.Offset;
            else if (LayoutNodeProperties.FloatOffset.Matches(property)) resolved = Property.FloatOffset;
            else if (LayoutNodeProperties.Padding.Matches(property)) resolved = Property.Padding;
            else if (LayoutNodeProperties.Gap.Matches(property)) resolved = Property.Gap;
            else if (LayoutNodeProperties.AspectRatio.Matches(property)) resolved = Property.AspectRatio;
            else if (LayoutNodeProperties.WidthValue.Matches(property)) resolved = Property.WidthValue;
            else if (LayoutNodeProperties.HeightValue.Matches(property)) resolved = Property.HeightValue;
            else if (LayoutNodeProperties.Direction.Matches(property)) resolved = Property.Direction;
            else if (LayoutNodeProperties.AlignX.Matches(property)) resolved = Property.AlignX;
            else if (LayoutNodeProperties.AlignY.Matches(property)) resolved = Property.AlignY;
            else
            {
                node = null;
                return false;
            }
            return true;
        }

        public override void Dispose()
        {
            _node = null;
        }

        // The setters dirty the tree themselves; there is nothing to tell the node afterwards.
        protected override bool TargetMustBeNotifiedOnWrite() => false;

        protected override bool TryReadFromTarget(out ValueContainer value)
        {
            value = default;
            if (_node == null) return false;
            switch (_property)
            {
                case Property.Offset: value = ValueContainer.From(_node.Offset); break;
                case Property.FloatOffset: value = ValueContainer.From(_node.FloatOffset); break;
                case Property.Padding: value = ValueContainer.From(_node.Padding); break;
                case Property.Gap: value = ValueContainer.FromFloat(_node.Gap); break;
                case Property.AspectRatio: value = ValueContainer.FromFloat(_node.AspectRatio); break;
                case Property.WidthValue: value = ValueContainer.FromFloat(_node.Width.Value); break;
                case Property.HeightValue: value = ValueContainer.FromFloat(_node.Height.Value); break;
                case Property.Direction: value = ValueContainer.FromEnum(_node.Direction); break;
                case Property.AlignX: value = ValueContainer.FromEnum(_node.AlignX); break;
                case Property.AlignY: value = ValueContainer.FromEnum(_node.AlignY); break;
                default: return false;
            }
            return true;
        }

        protected override bool TryWriteToTarget(ValueContainer value)
        {
            if (_node == null) return false;
            switch (_property)
            {
                case Property.Offset: _node.Offset = value.Vector2Value; break;
                case Property.FloatOffset: _node.FloatOffset = value.Vector2Value; break;
                case Property.Padding: _node.Padding = value.Vector4Value; break;
                case Property.Gap: _node.Gap = value.FloatValue; break;
                case Property.AspectRatio: _node.AspectRatio = value.FloatValue; break;
                case Property.WidthValue:
                {
                    var sizing = _node.Width;
                    sizing.Value = value.FloatValue;
                    _node.Width = sizing;
                    break;
                }
                case Property.HeightValue:
                {
                    var sizing = _node.Height;
                    sizing.Value = value.FloatValue;
                    _node.Height = sizing;
                    break;
                }
                case Property.Direction: _node.Direction = (LayoutDirection)value.EnumValue; break;
                case Property.AlignX: _node.AlignX = (AlignX)value.EnumValue; break;
                case Property.AlignY: _node.AlignY = (AlignY)value.EnumValue; break;
                default: return false;
            }
            return true;
        }

        private enum Property
        {
            Offset,
            FloatOffset,
            Padding,
            Gap,
            AspectRatio,
            WidthValue,
            HeightValue,
            Direction,
            AlignX,
            AlignY,
        }
    }

    // Registered once per domain, for the editor's property picker as well as for players. The registry keeps
    // its entries without a domain reload, so the flag must too.
    internal static class LayoutNodeBindingRegistration
    {
        private static bool s_registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void Register()
        {
            if (s_registered) return;
            s_registered = true;
            PropertyBindingRegistry.Register<LayoutNodePropertyBinding>(
                LayoutNodeProperties.All,
                (root, property) => new LayoutNodePropertyBinding(root, property));
        }
    }
}
#endif
