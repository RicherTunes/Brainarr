# CI Stability Guide

This document outlines best practices and guidelines to ensure the GitHub Actions CI pipeline remains stable and reliable.

## ✅ Current CI Status

The CI pipeline is now **stable and passing** across all environments:

- ✅ Ubuntu, Windows, macOS (latest versions)
- ✅ .NET 6.0.x and 8.0.x compatibility
- ✅ All tests passing with optimized timing
- ✅ Security scans completing successfully
- ✅ Zero build warnings

## 🎯 Key Stability Measures

### 1. **Test Timing Optimization**

- **Issue**: Rate limiter tests with realistic timing (2 req/min) took too long for CI
- **Solution**: Use faster but equivalent rates (6 req/3s) for CI environments
- **Rule**: All tests must complete within 30 seconds individually

### 2. **Async Pattern Compliance**

- **Issue**: Async methods without `await` causing warnings
- **Solution**: Always use `await Task.Yield()` or proper async operations
- **Rule**: No `async` method without `await` - use proper async patterns

### 3. **Assembly Dependency Strategy**

- **Success**: Pre-built Lidarr assemblies from GitHub releases
- **Avoid**: Building Lidarr from source (slow, error-prone)
- **Rule**: Always download pre-built assemblies with fallback URLs

### 4. **Test Isolation**

- **Pattern**: Each test is independent and self-contained
- **Resource Cleanup**: Proper disposal of resources in test teardown
- **Concurrency**: Tests can run in parallel without interference

## 🚫 What NOT to Do

### ❌ **Avoid These Patterns:**

1. **Long-running tests** (>30 seconds)
2. **Network dependencies** in unit tests
3. **Thread.Sleep()** instead of `await Task.Delay()`
4. **Flaky timing-dependent assertions**
5. **Hard-coded file paths** that vary by OS
6. **Resource leaks** (unclosed HTTP clients, etc.)

### ❌ **CI-Breaking Changes:**

1. Adding tests with `TimeSpan.FromMinutes(1)` or longer
2. Tests that depend on external services
3. Tests that write to system directories
4. Platform-specific assumptions

## 📋 Pre-Commit Checklist

Before pushing changes that might affect CI:

- [ ] All tests pass locally
- [ ] No compiler warnings
- [ ] Test timing is reasonable (<30s per test)
- [ ] No external dependencies in unit tests
- [ ] Proper async/await patterns used
- [ ] No resource leaks

## 🔧 CI Configuration

### Current Timeout Settings

- **Test hang timeout**: 5 minutes
- **Individual test timeout**: 30 seconds (recommended)
- **Build timeout**: 10 minutes

### Matrix Strategy

```yaml
matrix:
  os: [ubuntu-latest, windows-latest, macos-latest]
  dotnet-version: ['6.0.x', '8.0.x']
```

### Assembly Extraction Strategy (Preferred)

```bash
# Preferred: Extract from plugins Docker image
LIDARR_DOCKER_VERSION=${LIDARR_DOCKER_VERSION:-pr-plugins-2.14.2.4786}
mkdir -p ext/Lidarr/_output/net8.0
docker pull ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}
cid=$(docker create ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION})
for f in \
  Lidarr.dll \
  Lidarr.Common.dll \
  Lidarr.Core.dll \
  Lidarr.Http.dll \
  Lidarr.Api.V1.dll \
  Lidarr.Host.dll; do
  docker cp "$cid:/app/bin/$f" ext/Lidarr/_output/net8.0/ 2>/dev/null || echo "Optional: $f missing"
done
docker rm -f "$cid" >/dev/null
```

Fallback to release tarballs is allowed only when Docker is unavailable or when intentionally testing cross-version compatibility.

## 🎛️ Debugging CI Failures

### Common Failure Types

1. **Test Timeouts**
   - Check for long-running operations
   - Review rate limiting configurations
   - Ensure proper async patterns

2. **Assembly Not Found**
   - Verify Lidarr download completed
   - Check extraction was successful
   - Confirm assembly paths are correct

3. **Platform-Specific Failures**
   - Use cross-platform file paths (`Path.Combine`)
   - Avoid OS-specific system calls
   - Test on multiple platforms locally

4. **Flaky Tests**
   - Add retry logic for external dependencies
   - Use deterministic timing
   - Avoid race conditions

### Debug Commands

```bash
# Local test debugging
dotnet test --blame-hang-timeout 30s --logger:"console;verbosity=detailed"

# Assembly verification
ls -la ext/Lidarr/_output/net8.0/

# Build verification
dotnet build --verbosity normal
```

## 📈 Monitoring & Maintenance

### Regular Checks

- [ ] **Weekly**: Review CI run times and success rates
- [ ] **Monthly**: Update Lidarr fallback version if needed
- [ ] **Quarterly**: Review and update GitHub Actions versions

### Performance Targets

- **Total CI time**: <15 minutes
- **Test success rate**: >99%
- **Build time**: <5 minutes
- **Test time**: <10 minutes

## 🔄 Continuous Improvement

### Future Enhancements

1. **Parallel test execution** optimization
2. **Cache optimization** for dependencies
3. **Test result analysis** and reporting
4. **Performance regression detection**

### Metrics to Track

- CI run duration trends
- Test failure patterns
- Resource usage optimization
- Success rate by platform/version

---

## 📞 Support

If CI issues persist:

1. Check this guide first
2. Review recent commits for breaking changes
3. Compare with last successful run
4. Check GitHub Actions status page
5. File issue with detailed logs

**Remember**: A stable CI pipeline is crucial for team productivity and release confidence!
