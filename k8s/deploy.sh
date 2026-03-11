#!/bin/bash

# UIT-Go Kubernetes Deployment Script
# Deploys autoscaling and load distribution

set -e

echo "🚀 Deploying UIT-Go with Autoscaling & Load Distribution"
echo ""

# Check if kubectl is installed
if ! command -v kubectl &> /dev/null; then
    echo "❌ kubectl not found. Please install kubectl first."
    exit 1
fi

# Check if connected to cluster
if ! kubectl cluster-info &> /dev/null; then
    echo "❌ Not connected to Kubernetes cluster. Please configure kubectl."
    exit 1
fi

echo "✅ Connected to cluster"
echo ""

# 1. Create namespace
echo "📦 Creating namespace..."
kubectl apply -f namespace.yaml

# 2. Deploy API Gateway
echo "🌐 Deploying API Gateway..."
kubectl apply -f api-gateway-deployment.yaml
kubectl apply -f api-gateway-service.yaml
kubectl apply -f api-gateway-hpa.yaml

# 3. Deploy Driver Service
echo "🚗 Deploying Driver Service..."
kubectl apply -f driver-service.yaml

# 4. Deploy Ingress
echo "🔀 Deploying Ingress..."
kubectl apply -f ingress.yaml

echo ""
echo "✅ Deployment complete!"
echo ""
echo "📊 Checking status..."
kubectl get pods -n uit-go
echo ""
kubectl get hpa -n uit-go
echo ""
echo "🎉 Done! Your services will autoscale based on traffic."
echo ""
echo "Monitor autoscaling with:"
echo "  kubectl get hpa -n uit-go -w"
echo ""
echo "Watch pods scale:"
echo "  kubectl get pods -n uit-go -w"
