﻿using FreeBuild.Actions;
using FreeBuild.Geometry;
using FreeBuild.Model;
using Salamander.Actions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Salamander.BasicTools
{
    [Action(
        "CreateCircularSection",
        "Create a new section property with a circular profile",
        IconBackground = Resources.URIs.CircularSection,
        IconForeground = Resources.URIs.AddIcon)]
    public class CreateCircularSectionAction : ModelDocumentActionBase
    {
        [ActionInput(1, "the name of the section")]
        public string Name { get; set; } = "Circular Section";

        [ActionInput(2, "the depth of the section")]
        public double Diameter { get; set; } = 0.3;

        [ActionOutput(1, "the output section property")]
        public SectionFamily Section { get; set; }

        [ActionOutput(2, "the output section perimeter")]
        public Curve Perimeter
        {
            get { return Section?.Profile?.Perimeter; }
        }

        public override bool Execute(ExecutionInfo exInfo = null)
        {
            var profile = new CircularProfile(Diameter);
            Section = Model.Create.SectionFamily(Name, exInfo);
            Section.Profile = profile;
            return true;
        }
    }
}
