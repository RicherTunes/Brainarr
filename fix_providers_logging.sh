#!/bin/bash

# Fix logging in all provider files
for file in Brainarr.Plugin/Services/Providers/*.cs; do
    echo "Processing $file..."
    
    # Check if file contains error logging that needs to be fixed
    if grep -q "_logger.Error.*response.Content" "$file"; then
        # Create a temporary file with the fixes
        sed -i.bak \
            -e 's/_logger\.Error(\$".*API error: {response\.StatusCode} - {response\.Content}");/SecureLogger.LogError(_logger, SecureLogger.SanitizeHttpResponse((int)response.StatusCode, response.Content));/g' \
            -e 's/_logger\.Error(ex, "Error getting recommendations from .*");/SecureLogger.LogError(_logger, "Error getting recommendations", ex);/g' \
            -e 's/_logger\.Error(ex, ".*connection test failed");/SecureLogger.LogError(_logger, "Connection test failed", ex);/g' \
            -e 's/_logger\.Error(ex, "Failed to parse .* recommendations");/SecureLogger.LogError(_logger, "Failed to parse recommendations", ex);/g' \
            "$file"
        
        echo "  Fixed logging in $file"
    fi
done

echo "All provider files updated with secure logging"