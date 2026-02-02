# Security Policy

## Supported Versions

We actively maintain and provide security updates for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| Latest  | :white_check_mark: |
| < Latest | :x:               |

We recommend always using the [latest release](https://github.com/angrmgmt/Cliparino/releases/latest) for the best security and stability.

## Reporting a Vulnerability

If you discover a security vulnerability in Cliparino, please report it **privately** to help us address it before public disclosure.

### How to Report

**Email**: [angrmgmt@gmail.com](mailto:angrmgmt@gmail.com)

**Subject Line**: `[SECURITY] Vulnerability in Cliparino`

### Information to Include

Please provide as much detail as possible to help us understand and reproduce the issue:

1. **Description**: Clear description of the vulnerability
2. **Impact**: Potential security impact and severity assessment
3. **Reproduction Steps**: Detailed steps to reproduce the issue
4. **Affected Components**: Which parts of Cliparino are affected
5. **Environment**: 
   - Cliparino version
   - Streamer.bot version
   - OBS version
   - Operating system
6. **Proof of Concept**: Code snippets, logs, or screenshots (with sensitive data redacted)
7. **Suggested Fix**: If you have ideas for remediation

### What to Expect

1. **Acknowledgment**: We will acknowledge receipt within **48 hours**
2. **Assessment**: We will assess the vulnerability and determine severity
3. **Communication**: We will keep you informed of our progress
4. **Resolution**: We will develop and test a fix
5. **Disclosure**: Once fixed, we will:
   - Release a security update
   - Credit you in release notes (if desired)
   - Coordinate public disclosure timeline

### Security Best Practices

When using Cliparino:

- **Keep Updated**: Always use the latest version
- **Protect Tokens**: Never share Twitch API tokens or Streamer.bot credentials
- **Review Logs**: If sharing logs for support, redact sensitive information
- **Secure OBS**: Use OBS WebSocket authentication
- **Limit Access**: Restrict moderator permissions to trusted users

### Out of Scope

The following are generally **not** considered security vulnerabilities:

- Bugs that don't have security implications
- Issues in third-party dependencies (report to respective projects)
- Social engineering attacks against streamers/viewers
- Denial of service via Twitch chat spam (use Twitch moderation tools)

### Responsible Disclosure

We kindly request that you:

- **Do not** publicly disclose the vulnerability before we've had time to address it
- **Do not** exploit the vulnerability beyond what's necessary for demonstration
- **Give us reasonable time** to develop and release a fix (typically 90 days)

We appreciate your efforts to responsibly disclose findings and will acknowledge your contribution.

## Security Updates

Security updates are released as soon as possible after verification. We will:

- Tag releases with severity (e.g., `SECURITY: Critical Fix`)
- Document the issue in release notes
- Notify users via GitHub releases

## Questions or Concerns

For any security-related questions or concerns:

- **Email**: [angrmgmt@gmail.com](mailto:angrmgmt@gmail.com)
- **General Issues**: [GitHub Issues](https://github.com/angrmgmt/Cliparino/issues) (for non-security bugs)

Thank you for helping keep Cliparino and its users secure!
