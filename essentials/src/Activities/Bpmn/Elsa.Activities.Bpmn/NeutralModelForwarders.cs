using System.Runtime.CompilerServices;
using Elsa.Activities.Bpmn.Models;

// Neutral models moved to Core; authored ActivityNode structures require the Design package.
[assembly: TypeForwardedTo(typeof(BpmnElement))]
[assembly: TypeForwardedTo(typeof(BpmnElementTypes))]
[assembly: TypeForwardedTo(typeof(BpmnEventDefinition))]
[assembly: TypeForwardedTo(typeof(BpmnEventDefinitionProperties))]
[assembly: TypeForwardedTo(typeof(BpmnEventDefinitionTypes))]
[assembly: TypeForwardedTo(typeof(BpmnLane))]
[assembly: TypeForwardedTo(typeof(BpmnLoopCharacteristics))]
[assembly: TypeForwardedTo(typeof(BpmnMessageFlow))]
[assembly: TypeForwardedTo(typeof(BpmnPool))]
[assembly: TypeForwardedTo(typeof(BpmnRetainedAuthoring))]
[assembly: TypeForwardedTo(typeof(BpmnSequenceFlow))]
[assembly: TypeForwardedTo(typeof(BpmnStructure))]
