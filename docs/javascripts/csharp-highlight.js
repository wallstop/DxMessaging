/**
 * C# Semantic Syntax Highlighting Enhancement
 *
 * Pygments' C# lexer classifies many tokens as generic "Name" (.n) tokens,
 * making it impossible to differentiate types from methods from variables
 * with CSS alone. This script post-processes code blocks to add semantic
 * classes based on contextual patterns:
 *
 * - .n-type: Type names (PascalCase identifiers, often followed by < or at line start)
 * - .n-method: Method names (identifiers followed by < and ( or just ()
 * - .n-param: Parameter names (after type in parameter list, before = or , or ))
 * - .n-var: Variable names (other identifiers)
 */
(function () {
  "use strict";

  /** Maximum iterations for look-ahead loops to prevent hangs */
  var MAX_LOOKAHEAD = 50;

  /**
   * Check if a string is PascalCase (starts with uppercase)
   */
  function isPascalCase(text) {
    return /^[A-Z][a-zA-Z0-9]*$/.test(text);
  }

  /**
   * Check if a string is camelCase (starts with lowercase)
   */
  function isCamelCase(text) {
    return /^[a-z][a-zA-Z0-9]*$/.test(text);
  }

  /**
   * Safely get classList as array, handling elements without classList
   */
  function getClassList(element) {
    if (element && element.classList) {
      return Array.from(element.classList);
    }
    return [];
  }

  /**
   * Get the next sibling element (skipping whitespace spans)
   */
  function getNextNonWhitespace(element) {
    var next = element.nextElementSibling;
    while (next && next.classList && next.classList.contains("w")) {
      next = next.nextElementSibling;
    }
    return next;
  }

  /**
   * Get the previous sibling element (skipping whitespace spans)
   */
  function getPrevNonWhitespace(element) {
    var prev = element.previousElementSibling;
    while (prev && prev.classList && prev.classList.contains("w")) {
      prev = prev.previousElementSibling;
    }
    return prev;
  }

  /**
   * Process a single .n token and determine its semantic type
   */
  function classifyNameToken(span) {
    var text = span.textContent.trim();
    if (!text) return null;

    var next = getNextNonWhitespace(span);
    var prev = getPrevNonWhitespace(span);
    var nextText = next ? next.textContent.trim() : "";
    var prevText = prev ? prev.textContent.trim() : "";
    var nextClass = getClassList(next);
    var prevClass = getClassList(prev);

    // Method call: followed by (
    // Pattern: name(
    if (nextClass.includes("p") && nextText === "(") {
      return "n-method";
    }

    // Generic method or type: followed by < (operator)
    // Need to check if it's eventually followed by ( for method
    if (nextClass.includes("o") && nextText.startsWith("<")) {
      // Look ahead to see if there's a ( after the generic parameters
      var lookahead = next;
      var depth = 0;
      var iterations = 0;

      while (lookahead && iterations < MAX_LOOKAHEAD) {
        iterations++;
        var t = lookahead.textContent.trim();

        // Count generic bracket depth
        if (t === "<") depth++;
        if (t === ">") depth--;

        // Found ( after closing all generics - it's a method
        if (depth === 0 && t === "(") {
          return "n-method";
        }

        // Went negative or found definitive end
        if (depth < 0) break;

        lookahead = lookahead.nextElementSibling;

        // Stop at comments or end of line
        var lookaheadClass = getClassList(lookahead);
        if (lookaheadClass.includes("c1") || lookaheadClass.includes("cm")) {
          break;
        }
      }
      // If no ( found, it's likely a type
      return "n-type";
    }

    // Parameter: after another .n token (the type) and followed by , or ) or =
    if (
      (nextText === "," || nextText === ")" || nextText === "=") &&
      prevClass.includes("n") &&
      isCamelCase(text)
    ) {
      return "n-param";
    }

    // Parameter: after > (closing generic) and followed by , or ) or =
    if (
      (nextText === "," || nextText === ")" || nextText === "=") &&
      prevText === ">" &&
      isCamelCase(text)
    ) {
      return "n-param";
    }

    // Parameter: after ] (array type like int[]) and followed by , or ) or =
    if (
      (nextText === "," || nextText === ")" || nextText === "=") &&
      prevText === "]" &&
      isCamelCase(text)
    ) {
      return "n-param";
    }

    // Parameter: after ? (nullable type like int?) and followed by , or ) or =
    if (
      (nextText === "," || nextText === ")" || nextText === "=") &&
      prevText === "?" &&
      isCamelCase(text)
    ) {
      return "n-param";
    }

    // Parameter: after out/ref/in keywords
    if (
      prevClass.includes("k") &&
      (prevText === "out" || prevText === "ref" || prevText === "in") &&
      isCamelCase(text)
    ) {
      return "n-param";
    }

    // Type: PascalCase identifiers in various contexts
    if (isPascalCase(text)) {
      // After a keyword, it's likely a type (e.g., "class Foo", "new Bar")
      if (prevClass.includes("k") || prevClass.includes("kt")) {
        return "n-type";
      }

      // At the start of a code line (no previous sibling)
      if (!prev) {
        return "n-type";
      }

      // After punctuation like ( or < or , - likely a type in parameter list
      if (prevText === "(" || prevText === "<" || prevText === ",") {
        return "n-type";
      }

      // After . (dot) - member access, could be nested type or static member
      if (prevText === ".") {
        // If followed by < or ( it would have been caught above
        // Otherwise assume it's a type or type-like member
        return "n-type";
      }

      // Default: PascalCase is likely a type
      return "n-type";
    }

    // Variable/field: camelCase identifiers not caught above
    if (isCamelCase(text)) {
      return "n-var";
    }

    // Default: leave as-is (don't add any class)
    return null;
  }

  /**
   * Process all C# code blocks on the page
   */
  function enhanceCSharpHighlighting() {
    // Find all C# code blocks - handle both class orderings
    // .language-csharp.highlight is the actual structure from Pygments
    var codeBlocks = document.querySelectorAll(
      ".language-csharp.highlight code, .highlight.language-csharp code"
    );

    codeBlocks.forEach(function (codeBlock) {
      // Skip if already processed (idempotency)
      if (codeBlock.hasAttribute("data-semantic-enhanced")) {
        return;
      }
      codeBlock.setAttribute("data-semantic-enhanced", "true");

      // Find all .n spans (generic Name tokens)
      var nameSpans = codeBlock.querySelectorAll("span.n");

      nameSpans.forEach(function (span) {
        var semanticClass = classifyNameToken(span);
        if (semanticClass) {
          span.classList.add(semanticClass);
        }
      });
    });
  }

  // Run when DOM is ready
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", enhanceCSharpHighlighting);
  } else {
    enhanceCSharpHighlighting();
  }

  // Also run on navigation (for single-page app behavior in Material)
  if (typeof document$ !== "undefined") {
    document$.subscribe(function () {
      enhanceCSharpHighlighting();
    });
  }
})();
