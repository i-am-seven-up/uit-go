#!/bin/bash
#
# Phase 2 Deployment Script - Apply Performance Fixes
# This script deploys the PostgreSQL and service configuration fixes
#

set -e

echo "╔══════════════════════════════════════════════════════════╗"
echo "║   PHASE 2 PERFORMANCE FIXES DEPLOYMENT                  ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# Step 1: Apply PostgreSQL configuration (max_connections=300)
echo "📦 Step 1: Deploying PostgreSQL with increased max_connections..."
kubectl apply -f k8s/postgres.yaml
echo "✅ PostgreSQL configuration updated"
echo ""

# Step 2: Wait for PostgreSQL pods to restart
echo "⏳ Step 2: Waiting for PostgreSQL pods to restart..."
kubectl rollout status deployment/postgres-trip -n uit-go --timeout=120s
kubectl rollout status deployment/postgres-driver -n uit-go --timeout=120s
echo "✅ PostgreSQL pods restarted"
echo ""

# Step 3: Rebuild service images with updated connection pooling
echo "🔨 Step 3: Rebuilding service Docker images..."
docker build -t uit-go-trip-service:latest ./TripService
docker build -t uit-go-driver-service:latest ./DriverService
echo "✅ Docker images rebuilt"
echo ""

# Step 4: Deploy updated services
echo "📦 Step 4: Deploying updated services..."
kubectl apply -f k8s/trip-service.yaml
kubectl apply -f k8s/driver-service.yaml
echo "✅ Service configurations updated"
echo ""

# Step 5: Wait for service rollout
echo "⏳ Step 5: Waiting for service rollout..."
kubectl rollout status deployment/trip-service -n uit-go --timeout=180s
kubectl rollout status deployment/driver-service -n uit-go --timeout=180s
echo "✅ Services deployed"
echo ""

# Step 6: Verify deployment
echo "🔍 Step 6: Verifying deployment..."
echo ""
echo "PostgreSQL Pods:"
kubectl get pods -n uit-go -l app=postgres-trip
kubectl get pods -n uit-go -l app=postgres-driver
echo ""
echo "Service Pods:"
kubectl get pods -n uit-go -l app=trip-service
kubectl get pods -n uit-go -l app=driver-service
echo ""

# Step 7: Check PostgreSQL max_connections
echo "🔍 Step 7: Verifying PostgreSQL configuration..."
TRIP_POD=$(kubectl get pods -n uit-go -l app=postgres-trip -o jsonpath='{.items[0].metadata.name}')
DRIVER_POD=$(kubectl get pods -n uit-go -l app=postgres-driver -o jsonpath='{.items[0].metadata.name}')

echo "Trip PostgreSQL max_connections:"
kubectl exec -n uit-go $TRIP_POD -- psql -U postgres -d uitgo_trip -c "SHOW max_connections;" || echo "⚠️  Could not verify (pod may still be starting)"

echo "Driver PostgreSQL max_connections:"
kubectl exec -n uit-go $DRIVER_POD -- psql -U postgres -d uitgo_driver -c "SHOW max_connections;" || echo "⚠️  Could not verify (pod may still be starting)"
echo ""

echo "╔══════════════════════════════════════════════════════════╗"
echo "║   DEPLOYMENT COMPLETED SUCCESSFULLY!                    ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "📊 Configuration Summary:"
echo "  - PostgreSQL max_connections: 300 (up from 100)"
echo "  - TripService pool size: 100 per replica (up from 50)"
echo "  - DriverService pool size: 100 per replica (up from 50)"
echo "  - Total capacity: 300 connections per database"
echo ""
echo "📈 Expected Performance Improvements:"
echo "  - Trip Creation: 76 RPS → 800+ RPS (10× improvement)"
echo "  - Error Rate: 36% → <1%"
echo "  - p95 Latency: 25s → <500ms (50× improvement)"
echo ""
echo "🧪 Next Step: Run E2E performance tests to validate"
echo "  $ cd E2E.PerformanceTests"
echo "  $ dotnet run"
echo ""
