# Problem

## Overview

There is a need to enable programmatic and operational access into a restricted AWS network environment without granting users direct VPN access.

The target environment is an AWS VPC that contains protected systems, internal services, operational infrastructure, and potentially sensitive customer data.

## Core Problem

Users need a practical way to perform work inside a restricted AWS environment, but that access must not allow sensitive data or operational artifacts to move freely onto unmanaged local machines.

Direct VPN access creates an undesirable access model because it extends the restricted network boundary to the user's local workstation. Once that happens, customer data can be copied, processed, or stored outside the governance boundaries of the AWS account.

The core problem is:

> How can users perform real operational and development work inside a restricted AWS environment while keeping data, tooling, and workflows inside governed AWS-managed infrastructure?

## Governance Problem

Arbitrary user-accessible machines create governance risk.

If users can create or access unmanaged systems freely, sensitive data and operational artifacts may become scattered across infrastructure that is difficult to track, govern, and audit.

This creates problems such as:

- Customer data appearing in uncontrolled locations
- Operational artifacts persisting without clear ownership
- Infrastructure drift
- Inconsistent lifecycle management
- Weak auditability
- Unclear responsibility for cleanup
- Increased risk from unmanaged systems

## Ergonomics Problem

Strictly disposable environments are operationally clean, but they can be too fragile if normal platform disruptions wipe out useful in-progress work immediately.

For example:

- a container restart should not necessarily erase all useful local state
- an accidental OOM event should not create disproportionate user pain
- a routine reschedule should not make the environment unusable by default

At the same time, the platform should not drift into permanent personal servers.

This creates a conflict between:

- Infrastructure lifecycle
- User productivity
- Operational safety
- Simplicity of platform management

## Balancing Requirement

The system must balance two competing needs:

- Strong governance, containment, and operational safety
- Practical support for real-world browser-based work inside AWS

The first version should focus on a simple managed workspace model that is resilient enough for common failures without turning each workspace into a durable personal machine.

If future workloads clearly outgrow that model, they can be handled by a separately designed execution model rather than by weakening the workspace boundary by default.

## Desired Separation

The immediate problem requires a clear separation between:

- User-facing browser workspaces
- Platform-managed provisioning, lifecycle, and cleanup

Interactive environments should remain disposable, isolated, and controlled.

If a future need emerges for longer-lived execution environments, those should be introduced under their own governance model rather than assumed as part of the initial platform shape.
