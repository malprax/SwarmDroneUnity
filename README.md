# SwarmDroneUnity

Physics-Based Probabilistic Swarm Drone Simulation for Indoor Object Search

## Overview

This repository provides the complete simulation framework and source code
used in the journal article:

**“Probabilistic Modeling and Experimental Validation of Swarm Drones for Object Search Missions under Dynamic Performance Variations”**

The project implements a physics-based indoor swarm drone simulation using a
fixed leader–slave coordination structure combined with probabilistic
decision-making under explicit mechanical feasibility constraints. The
simulation is intended to study the interaction between stochastic
decision policies, quadrotor dynamics, collision behavior, and environmental
uncertainty in confined indoor environments.

All simulation results, figures, and statistical analyses reported in the
article are generated using this repository.

---

## Key Features

- Physics-based indoor quadrotor simulation implemented in the Unity engine
- Fixed leader–slave swarm coordination architecture
- Probabilistic belief-based object search
- Monte Carlo rollout-based decision evaluation
- Explicit mechanical feasibility constraints, including:
  - Thrust limits
  - Rotor speed bounds
  - Rigid-body dynamics
- Collision-aware motion execution and logging
- Large-scale Monte Carlo experimentation (N = 1000 runs)

---

## System Architecture

The swarm consists of three quadrotor agents operating under fixed roles:

- **Leader drone**
  - Global belief aggregation
  - Coordination and mission supervision
  - Monitoring of overall swarm progress

- **Slave drones**
  - Local exploration and navigation
  - Sensing and target detection
  - Execution of mechanically constrained motion commands

Decision-making at the agent level is explicitly filtered through mechanical
constraints to ensure that all executed actions remain physically realizable
for indoor quadrotor platforms.

---

## Simulation Environment

The simulated indoor environment consists of:
- A central home base
- Multiple enclosed rooms
- Narrow passages and wall boundaries

This configuration is intentionally designed to introduce:
- Partial observability
- Restricted maneuvering space
- Elevated collision risk

The environment geometry and spatial constraints correspond directly to those
used in the experimental evaluation presented in the associated journal
article.

---

## Repository Structure

```text
SwarmDroneUnity/
├── Assets/
│   ├── Scripts/          # Swarm logic, probabilistic decision-making, dynamics
│   ├── Scenes/           # Indoor environments and simulation scenes
│   ├── Prefabs/          # Drone and environment prefabs
│   └── Materials/        # Visual and physical materials
├── ProjectSettings/      # Unity project configuration
├── Packages/             # Unity package dependencies
├── README.md             # Project documentation